using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> whose
/// <see cref="IrTypeSemantics.IsBranded"/> is set into the canonical
/// TypeScript brand-type + companion-namespace shape:
/// <code>
/// export type UserId = string &amp; { readonly __brand: "UserId" };
/// export namespace UserId {
///     export function create(value: string): UserId { return value as UserId; }
///     // toString(value) only when the wrapped primitive isn't already string
///     // ...plus any user-declared static methods on the struct
/// }
/// </code>
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
    /// <summary>The non-validating factory every branded companion synthesizes.</summary>
    internal const string CreateMember = "create";

    /// <summary>The string conversion a non-string branded companion synthesizes.</summary>
    internal const string ToStringMember = "toString";

    internal static void Convert(
        IrClassDeclaration ir,
        TsCompanionPlan plan,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry = null
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
        // exported functions in the companion namespace.
        foreach (var group in plan.Groups)
            functions.AddRange(ConvertStaticMethodGroup(group, bclRegistry));

        sink.Add(new TsNamespaceDeclaration(tsTypeName, functions));
    }

    /// <summary>
    /// Lowers one static group as the plan names it. A single method becomes
    /// one function; two or more go through the shared overload dispatcher so
    /// no sibling is silently dropped.
    /// </summary>
    private static IReadOnlyList<TsFunction> ConvertStaticMethodGroup(
        TsCompanionGroup group,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        group.IsDispatched
            ? IrToTsOverloadDispatcherBridge.BuildNamespaceFunctions(group, bclRegistry)
            : [ConvertStaticMethod(group.Overloads[0].Method, group.FunctionName, bclRegistry)];

    private static TsFunction ConvertStaticMethod(
        IrMethodDeclaration method,
        string functionName,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new(
            functionName,
            IrToTsOverloadDispatcherBridge.MapParameters(method),
            IrToTsTypeMapper.Map(method.ReturnType),
            IrToTsBodyHelpers.LowerOrNotImplemented(
                method.Body,
                method.Name,
                IrToTsBridgeContext.For(bclRegistry)
            ),
            Exported: true,
            Async: method.Semantics.IsAsync
        );
}
