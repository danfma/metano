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
        if (!ir.Semantics.IsBranded || ir.Semantics.BrandedUnderlyingType is null)
            return false;

        var tsTypeName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var primitiveType = IrToTsTypeMapper.Map(ir.Semantics.BrandedUnderlyingType);
        // The brand-type pattern only makes sense over a TS primitive — anything
        // else can't be intersected with `{ readonly __brand: … }` and stay
        // assignable. Bail to the regular class/record path in that case.
        if (primitiveType is not (TsStringType or TsNumberType or TsBooleanType or TsBigIntType))
            return false;

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
        if (ir.Members is { Count: > 0 } members)
        {
            foreach (var member in members)
            {
                if (member is not IrMethodDeclaration { IsStatic: true } m)
                    continue;
                functions.AddRange(ConvertStaticMethodGroup(m, ir.Semantics, bclRegistry));
            }
        }

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
    private static IReadOnlyList<TsFunction> ConvertStaticMethodGroup(
        IrMethodDeclaration primary,
        IrTypeSemantics semantics,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var group = new[] { primary }
            .Concat(primary.Overloads ?? [])
            .Where(method => !IsIdentityUnwrap(method, semantics))
            .ToList();

        if (group.Count == 0)
            return [];

        if (group.Count == 1)
            return [ConvertStaticMethod(group[0], bclRegistry)];

        var dispatcherName = IrToTsNamingPolicy.ToFunctionName(group[0].Name, group[0].Attributes);

        return IrToTsOverloadDispatcherBridge.BuildNamespaceFunctions(
            group[0] with
            {
                Overloads = group.Skip(1).ToList(),
            },
            dispatcherName,
            bclRegistry
        );
    }

    /// <summary>
    /// A conversion operator from the wrapper to its underlying primitive
    /// (<c>implicit operator string(UserId self) => self.Value</c>) is the
    /// identity once the wrapper lowers to <c>primitive &amp; { __brand }</c>,
    /// and call sites never invoke it — the companion skips it. Wrap-direction
    /// conversions (<c>explicit operator UserId(string raw)</c>) return the
    /// wrapper, not the primitive, so they are kept: they can carry
    /// validation.
    /// </summary>
    private static bool IsIdentityUnwrap(IrMethodDeclaration method, IrTypeSemantics semantics) =>
        method.Semantics is { IsOperator: true, OperatorKind: "Implicit" or "Explicit" }
        && method.ReturnType.Equals(semantics.BrandedUnderlyingType);

    private static TsFunction ConvertStaticMethod(
        IrMethodDeclaration method,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var parameters = method
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                IrToTsTypeMapper.Map(p.Type),
                Rest: p.IsParams
            ))
            .ToList();
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry);

        // Inline wrapper helpers lower to `export namespace TypeName { export
        // function methodName() { … } }`. Function declarations inside a TS
        // namespace can NOT use reserved words (`function new() {}` is illegal
        // even though `obj.new` is fine), so this stays on the escaping
        // ToCamelCase variant. The call-site bridge detects [Branded]
        // receivers and matches the escape so the two halves stay in sync.
        var name = IrToTsNamingPolicy.ToFunctionName(method.Name, method.Attributes);

        return new TsFunction(
            name,
            parameters,
            IrToTsTypeMapper.Map(method.ReturnType),
            body,
            Exported: true,
            Async: method.Semantics.IsAsync
        );
    }
}
