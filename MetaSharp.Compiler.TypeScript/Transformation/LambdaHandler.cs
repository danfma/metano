using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Lowers C# lambda expressions (simple <c>x =&gt; ...</c> and parenthesized
/// <c>(x, y) =&gt; ...</c> forms) into <see cref="TsArrowFunction"/>.
///
/// Parameter types are resolved from the semantic model when available, falling back to
/// <see cref="TsAnyType"/> when not. Lambda bodies are either single expressions
/// (returned implicitly) or block bodies (forwarded to the parent's
/// <see cref="ExpressionTransformer.TransformStatement"/>).
/// </summary>
public sealed class LambdaHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsArrowFunction TransformSimpleLambda(SimpleLambdaExpressionSyntax lambda)
    {
        var param = TransformLambdaParameter(lambda.Parameter);
        var body = TransformLambdaBody(lambda.Body);
        var isAsync = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
        return new TsArrowFunction([param], body, isAsync);
    }

    public TsArrowFunction TransformParenthesizedLambda(ParenthesizedLambdaExpressionSyntax lambda)
    {
        var parameters = lambda.ParameterList.Parameters
            .Select(TransformLambdaParameter)
            .ToList();
        var body = TransformLambdaBody(lambda.Body);
        var isAsync = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
        return new TsArrowFunction(parameters, body, isAsync);
    }

    private TsParameter TransformLambdaParameter(ParameterSyntax param)
    {
        var name = TypeScriptNaming.ToCamelCase(param.Identifier.Text);

        // Try to resolve the type from the semantic model
        var symbol = _parent.Model.GetDeclaredSymbol(param);
        TsType? type;
        if (symbol is IParameterSymbol paramSymbol)
        {
            // [NoEmit] parameter types are ambient — we have no TS name to emit and we
            // don't want to import them. Drop the annotation entirely so TypeScript
            // infers the type from the call-site context (e.g., the imported function's
            // .d.ts signature).
            if (IsNoEmitType(paramSymbol.Type))
                return new TsParameter(name, null);

            type = TypeMapper.Map(paramSymbol.Type);
        }
        else if (param.Type is not null)
        {
            var typeInfo = _parent.Model.GetTypeInfo(param.Type);
            if (typeInfo.Type is not null && IsNoEmitType(typeInfo.Type))
                return new TsParameter(name, null);
            type = typeInfo.Type is not null ? TypeMapper.Map(typeInfo.Type) : new TsAnyType();
        }
        else
        {
            type = new TsAnyType();
        }

        return new TsParameter(name, type);
    }

    private static bool IsNoEmitType(ITypeSymbol type) =>
        type is INamedTypeSymbol named && SymbolHelper.HasNoEmit(named);

    private IReadOnlyList<TsStatement> TransformLambdaBody(CSharpSyntaxNode body)
    {
        return body switch
        {
            BlockSyntax block => block.Statements.Select(_parent.TransformStatement).ToList(),
            ExpressionSyntax expr => [new TsReturnStatement(_parent.TransformExpression(expr))],
            _ => [new TsReturnStatement(new TsIdentifier("undefined"))],
        };
    }
}
