using System.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> whose
/// <see cref="IrTypeSemantics.IsBranded"/> is set into the canonical
/// TypeScript brand type plus its companion object (spec 011 FR-040):
/// <code>
/// export type UserId = string &amp; { readonly __brand: "UserId" };
/// function UserId$create(value: string): UserId { return value as UserId; }
/// // UserId$toString(value) only when the wrapped primitive isn't already string
/// // ...plus any user-declared static methods on the struct
/// export const UserId = { create: UserId$create, … } as const;
/// </code>
/// The companion is a pure object literal, so a bundle drops it when nothing
/// uses it, where a TypeScript <c>namespace</c> compiles to an IIFE every
/// bundler keeps; call sites (<c>UserId.create(x)</c>) are the same. Each
/// function is module-level under <c>Type$member</c>, which no C# name or
/// import spells.
/// The caller builds the <see cref="TsCompanionPlan"/> first: a type without
/// one (no <c>IsBranded</c> flag, or a wrapped type a brand cannot intersect)
/// falls through to the next per-shape handler.
/// <para>
/// A static overload group lowers to an exported dispatcher plus one exported
/// function per overload: a call transpiled from C# names the function of
/// the overload C# bound, since the dispatcher's runtime checks cannot tell
/// every pair apart (issue #569); the dispatcher stays for hand-written
/// TypeScript and for a consumer reading the type from metadata.
/// </para>
/// </summary>
public static class IrToTsBrandedBridge
{
    /// <inheritdoc cref="TsCompanionPlan.CreateMember"/>
    internal const string CreateMember = TsCompanionPlan.CreateMember;

    /// <inheritdoc cref="TsCompanionPlan.ToStringMember"/>
    internal const string ToStringMember = TsCompanionPlan.ToStringMember;

    internal static void Convert(
        IrClassDeclaration ir,
        TsCompanionPlan plan,
        List<TsTopLevel> sink,
        IrToTsBridgeContext context
    )
    {
        var tsTypeName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var primitiveType = plan.PrimitiveType;

        // export type UserId = string & { readonly __brand: "UserId" };
        var brandType = new TsNamedType($"{{ readonly __brand: \"{tsTypeName}\" }}");
        sink.Add(new TsTypeAlias(tsTypeName, new TsIntersectionType([primitiveType, brandType])));

        var functions = new List<TsFunction>
        {
            // create(value: T): TypeName  →  return value as TypeName;
            new(
                CreateMember,
                [new TsParameter("value", primitiveType)],
                new TsNamedType(tsTypeName),
                [
                    new TsReturnStatement(
                        new TsCastExpression(new TsIdentifier("value"), new TsNamedType(tsTypeName))
                    ),
                ],
                Exported: true
            ),
        };

        // toString(value: TypeName): string — only when the wrapped primitive
        // isn't already a string (otherwise it'd just be the identity).
        if (plan.HasToString)
        {
            functions.Add(
                new TsFunction(
                    ToStringMember,
                    [new TsParameter("value", new TsNamedType(tsTypeName))],
                    new TsStringType(),
                    [
                        new TsReturnStatement(
                            new TsCallExpression(
                                new TsIdentifier("String"),
                                [new TsIdentifier("value")]
                            )
                        ),
                    ],
                    Exported: true
                )
            );
        }

        // User-declared static methods on the wrapper struct. Visibility +
        // [Ignore] + [Emit] filtering already happened in the extractor; we
        // just emit each remaining static method (or overload group) as
        // companion functions, under the names the plan claimed, bound to
        // the methods of this IR.
        var companion = new TsIdentifier(tsTypeName);

        foreach (var group in plan.Bind(ir))
            functions.AddRange(ConvertStaticMethodGroup(group, companion, context));

        sink.AddRange(functions.Select(function => AsModuleFunction(function, tsTypeName)));
        sink.Add(CompanionObject(tsTypeName, functions));
    }

    /// <summary>
    /// <c>export const Money = { create: Money$create, … } as const;</c>. A member's
    /// JSDoc (the <c>@internal</c> of an internal or private member) belongs to the
    /// companion key, not to the module function no consumer names; declaration emit
    /// strips a key only from a type annotation, so a companion with a tagged member is
    /// annotated with its written shape instead of <c>as const</c>, and
    /// <c>stripInternal</c> drops that key from the declarations.
    /// </summary>
    private static TsConstObject CompanionObject(
        string typeName,
        IReadOnlyList<TsFunction> functions
    )
    {
        var entries = functions
            .Select(function => new TsConstObjectEntry(
                function.Name,
                new TsIdentifier(ModuleName(typeName, function.Name))
            ))
            .ToList();

        if (functions.All(function => function.JSDoc is null))
            return new TsConstObject(typeName, entries);

        var shape = new TsObjectType([
            .. functions.Select(function => new TsObjectTypeField(
                function.Name,
                new TsTypeQuery(ModuleName(typeName, function.Name)),
                Readonly: true,
                JSDoc: function.JSDoc
            )),
        ]);

        return new TsConstObject(typeName, entries, Shape: shape);
    }

    /// <summary>The module-level name of a companion member: <c>Type$member</c>.</summary>
    private static string ModuleName(string typeName, string member) =>
        $"{typeName}{ModuleLocalNaming.Separator}{member}";

    private static TsFunction AsModuleFunction(TsFunction function, string typeName) =>
        function with
        {
            Name = ModuleName(typeName, function.Name),
            Exported = false,
            JSDoc = null,
        };

    /// <summary>
    /// Lowers one static group as the plan names it. A single method becomes
    /// one function; two or more go through the shared overload dispatcher so
    /// no sibling is silently dropped.
    /// </summary>
    private static IReadOnlyList<TsFunction> ConvertStaticMethodGroup(
        TsCompanionGroup group,
        TsExpression companion,
        IrToTsBridgeContext context
    ) =>
        group switch
        {
            TsCompanionDispatcher dispatcher =>
                IrToTsOverloadDispatcherBridge.BuildCompanionFunctions(
                    dispatcher,
                    companion,
                    context
                ),
            TsCompanionFunction function =>
            [
                ConvertStaticMethod(function.Method, function.FunctionName, context),
            ],
            _ => throw new UnreachableException(
                $"Unexpected companion group {group.GetType().Name}."
            ),
        };

    private static TsFunction ConvertStaticMethod(
        IrMethodDeclaration method,
        string functionName,
        IrToTsBridgeContext context
    ) =>
        new(
            functionName,
            IrToTsOverloadDispatcherBridge.FastPathParameters(method, context),
            IrToTsTypeMapper.Map(method.ReturnType),
            IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, context),
            Exported: true,
            Async: method.Semantics.IsAsync
        );
}
