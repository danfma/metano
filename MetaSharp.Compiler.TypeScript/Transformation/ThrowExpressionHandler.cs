using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Lowers C# <c>throw</c> <em>expressions</em> (the expression-position form,
/// e.g., <c>x ?? throw new ArgumentNullException(...)</c>) into TypeScript.
///
/// JavaScript only allows <c>throw</c> as a statement, so the expression form is wrapped
/// in an immediately-invoked arrow function whose single statement is the actual throw:
/// <code>
/// (() => { throw new SomeError(...); })()
/// </code>
///
/// Statement-position throws (<c>throw new SomeError(...)</c>) are handled by
/// <see cref="StatementHandler"/> instead.
/// </summary>
public sealed class ThrowExpressionHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression Transform(ThrowExpressionSyntax throwExpr) =>
        new TsCallExpression(
            new TsArrowFunction(
                [],
                [new TsThrowStatement(_parent.TransformExpression(throwExpr.Expression))]
            ),
            []
        );
}
