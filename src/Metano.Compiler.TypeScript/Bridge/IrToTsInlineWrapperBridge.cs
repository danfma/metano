using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> whose
/// <see cref="IrTypeSemantics.IsInlineWrapper"/> is set into the canonical
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
/// <c>IsInlineWrapper</c> flag, or a missing <c>InlineWrappedType</c>),
/// so the caller can fall through to the next per-shape handler.
/// </summary>
public static class IrToTsInlineWrapperBridge
{
    public static bool Convert(
        IrClassDeclaration ir,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        if (!ir.Semantics.IsInlineWrapper || ir.Semantics.InlineWrappedType is null)
            return false;

        var tsTypeName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var primitiveType = IrToTsTypeMapper.Map(ir.Semantics.InlineWrappedType);
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
        // just emit each remaining static method as an exported function in
        // the companion namespace.
        if (ir.Members is { Count: > 0 } members)
        {
            foreach (var member in members)
            {
                if (member is not IrMethodDeclaration m || !m.IsStatic)
                    continue;
                functions.Add(ConvertStaticMethod(m, bclRegistry));
            }
        }

        sink.Add(new TsNamespaceDeclaration(tsTypeName, functions));
        return true;
    }

    private static TsFunction ConvertStaticMethod(
        IrMethodDeclaration method,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var parameters = method
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                IrToTsTypeMapper.Map(p.Type)
            ))
            .ToList();
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry);

        // Inline wrapper helpers lower to `export namespace TypeName { export
        // function methodName() { … } }`. Function declarations inside a TS
        // namespace can NOT use reserved words (`function new() {}` is illegal
        // even though `obj.new` is fine), so this stays on the escaping
        // ToCamelCase variant. The call-site bridge detects [InlineWrapper]
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
