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
/// Returns <c>false</c> when the IR doesn't describe a wrapper (no
/// <c>IsBranded</c> flag, or a missing <c>BrandedUnderlyingType</c>),
/// so the caller can fall through to the next per-shape handler.
/// </summary>
public static class IrToTsBrandedBridge
{
    public static bool Convert(
        IrClassDeclaration ir,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        // The brand-type pattern only makes sense over a primitive — anything
        // else can't be intersected with `{ readonly __brand: … }` and stay
        // assignable. Bail to the regular class/record path in that case. The
        // extractor gates the `id.Value` → `id` collapse on the same rule.
        if (!ir.Semantics.IsBranded || !ir.Semantics.BrandedUnderlyingType.IsBrandablePrimitive())
            return false;

        var tsTypeName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var primitiveType = IrToTsTypeMapper.Map(ir.Semantics.BrandedUnderlyingType);

        // export type UserId = string & { readonly __brand: "UserId" };
        var brandType = new TsNamedType($"{{ readonly __brand: \"{tsTypeName}\" }}");
        sink.Add(new TsTypeAlias(tsTypeName, new TsIntersectionType([primitiveType, brandType])));

        var functions = new List<TsFunction>
        {
            // create(value: T): TypeName  →  return value as TypeName;
            new(
                "create",
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
        if (primitiveType is not TsStringType)
        {
            functions.Add(
                new TsFunction(
                    "toString",
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
        var staticMethods = (ir.Members ?? [])
            .OfType<IrMethodDeclaration>()
            .Where(method => method.IsStatic)
            .ToList();

        var takenNames = functions
            .Select(function => function.Name)
            .Concat(staticMethods.Select(FunctionName))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var method in staticMethods)
            functions.AddRange(
                ConvertStaticMethodGroup(method, ir.Semantics, takenNames, bclRegistry)
            );

        sink.Add(new TsNamespaceDeclaration(tsTypeName, functions));
        return true;
    }

    /// <summary>
    /// Lowers a static method and the overload siblings folded onto
    /// <see cref="IrMethodDeclaration.Overloads"/>. A single surviving method
    /// becomes one function; two or more go through the shared overload
    /// dispatcher so no sibling is silently dropped. Identity unwrap
    /// conversions are filtered first (see <see cref="IsIdentityUnwrap"/>).
    /// </summary>
    /// <param name="takenNames">
    /// Every function name already claimed in the companion namespace; the
    /// dispatcher's namespace-local fast paths are disambiguated against it
    /// and added to it.
    /// </param>
    private static IReadOnlyList<TsFunction> ConvertStaticMethodGroup(
        IrMethodDeclaration primary,
        IrTypeSemantics semantics,
        ISet<string> takenNames,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var group = new[] { primary }
            .Concat(primary.Overloads ?? [])
            .Where(method => !IsIdentityUnwrap(method, semantics))
            .ToList();

        return group.Count switch
        {
            0 => [],
            1 => [ConvertStaticMethod(group[0], bclRegistry)],
            _ => IrToTsOverloadDispatcherBridge.BuildNamespaceFunctions(
                group,
                FunctionName(group[0]),
                takenNames,
                bclRegistry
            ),
        };
    }

    /// <summary>
    /// A conversion operator from the wrapper to its underlying primitive
    /// (<c>implicit operator string(UserId self) => self.Value</c>, or its
    /// nullable-annotated <c>string?</c> form) is the identity once the
    /// wrapper lowers to <c>primitive &amp; { __brand }</c>, and call sites
    /// never invoke it — the companion skips it. Wrap-direction conversions
    /// (<c>explicit operator UserId(string raw)</c>) return the wrapper, not
    /// the primitive, so they are kept: they can carry validation.
    /// </summary>
    private static bool IsIdentityUnwrap(IrMethodDeclaration method, IrTypeSemantics semantics) =>
        method.Semantics is { IsOperator: true, OperatorKind: "Implicit" or "Explicit" }
        && StripNullable(method.ReturnType).Equals(semantics.BrandedUnderlyingType);

    private static IrTypeRef StripNullable(IrTypeRef type) =>
        type is IrNullableTypeRef nullable ? nullable.Inner : type;

    /// <summary>
    /// Inline wrapper helpers lower to <c>export namespace TypeName { export
    /// function methodName() { … } }</c>. Function declarations inside a TS
    /// namespace can NOT use reserved words (<c>function new() {}</c> is
    /// illegal even though <c>obj.new</c> is fine), so this stays on the
    /// escaping naming policy. The call-site bridge detects [Branded]
    /// receivers and matches the escape so the two halves stay in sync.
    /// </summary>
    private static string FunctionName(IrMethodDeclaration method) =>
        IrToTsNamingPolicy.ToFunctionName(method.Name, method.Attributes);

    private static TsFunction ConvertStaticMethod(
        IrMethodDeclaration method,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new(
            FunctionName(method),
            IrToTsOverloadDispatcherBridge.MapParameters(method),
            IrToTsTypeMapper.Map(method.ReturnType),
            IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry),
            Exported: true,
            Async: method.Semantics.IsAsync
        );
}
