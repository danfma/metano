using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> whose
/// <see cref="IrTypeSemantics.IsException"/> is set into a TypeScript class
/// extending the JS <c>Error</c> built-in (or another transpilable exception
/// base). The constructor mirrors the C# constructor with the most parameters
/// and emits a single <c>super(...)</c> call. Base-call arguments come from
/// <see cref="IrConstructorDeclaration.BaseArguments"/> when the source
/// declared a primary-constructor base initializer; otherwise the first
/// constructor parameter is forwarded to <c>super</c> as a sensible default.
/// </summary>
public static class IrToTsExceptionBridge
{
    public static bool Convert(
        IrClassDeclaration ir,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        if (!ir.Semantics.IsException)
            return false;

        var ctorParams = new List<TsConstructorParam>();
        var superArgs = new List<TsExpression>();

        if (ir.Constructor is { } ctor)
        {
            foreach (var p in ctor.Parameters)
            {
                ctorParams.Add(
                    new TsConstructorParam(
                        TypeScriptNaming.ToCamelCase(p.Parameter.Name),
                        IrToTsTypeMapper.Map(p.Parameter.Type),
                        Accessibility: TsAccessibility.None
                    )
                );
            }

            if (ctor.BaseArguments is { Count: > 0 } baseArgs)
            {
                foreach (var arg in baseArgs)
                    superArgs.Add(IrToTsExpressionBridge.Map(arg.Value, bclRegistry));
            }
        }

        // No explicit base initializer — forward the first ctor param to
        // super as a sensible default (typically the message).
        if (superArgs.Count == 0 && ctorParams.Count > 0)
            superArgs.Add(new TsIdentifier(ctorParams[0].Name));

        var ctorBody = new List<TsStatement>
        {
            new TsExpressionStatement(new TsCallExpression(new TsIdentifier("super"), superArgs)),
        };
        var constructor = new TsConstructor(ctorParams, ctorBody);

        // Resolve the TS extends clause: an explicitly declared transpilable
        // exception base (e.g., a user-defined exception that itself extends
        // System.Exception) keeps its name; everything else falls back to the
        // JS Error built-in so the runtime shape stays compatible with
        // `instanceof Error` checks.
        var extendsType = ir.BaseType
            is IrNamedTypeRef
            {
                Semantics: { Kind: IrNamedTypeKind.Exception, IsTranspilable: true },
            }
            ? IrToTsTypeMapper.Map(ir.BaseType)
            : new TsNamedType("Error");

        var name = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        sink.Add(new TsClass(name, constructor, [], Extends: extendsType));
        return true;
    }
}
