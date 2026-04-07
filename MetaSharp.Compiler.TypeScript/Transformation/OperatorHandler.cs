using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Lowers C# operator expressions into TypeScript equivalents:
/// <list type="bullet">
///   <item>
///     <see cref="BinaryExpressionSyntax"/> — covers both ordinary binary operators and
///     the legacy <c>x is Type</c> form (lowered to <c>x instanceof Type</c>; the modern
///     pattern-matching <c>x is Type t</c> is handled by <see cref="PatternMatchingHandler"/>
///     instead).
///   </item>
///   <item><see cref="AssignmentExpressionSyntax"/> — <c>=</c>, compound assignments, and
///   <c>??=</c>.</item>
///   <item><see cref="PrefixUnaryExpressionSyntax"/> — <c>-x</c>, <c>!x</c>, <c>++x</c>, …</item>
/// </list>
///
/// The C# → TS operator name table is small (only <c>==</c>/<c>!=</c> become
/// <c>===</c>/<c>!==</c>; everything else passes through), so it lives as private static
/// helpers on this handler.
/// </summary>
public sealed class OperatorHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression TransformBinary(BinaryExpressionSyntax bin)
    {
        // x is Type (old-style, before pattern matching) → x instanceof Type
        if (bin.OperatorToken.Text == "is")
        {
            return new TsBinaryExpression(
                _parent.TransformExpression(bin.Left),
                "instanceof",
                new TsIdentifier(bin.Right.ToString()) // keep PascalCase for type name
            );
        }

        return new TsBinaryExpression(
            _parent.TransformExpression(bin.Left),
            MapBinaryOperator(bin.OperatorToken.Text),
            _parent.TransformExpression(bin.Right)
        );
    }

    public TsExpression TransformAssignment(AssignmentExpressionSyntax assign) =>
        new TsBinaryExpression(
            _parent.TransformExpression(assign.Left),
            MapAssignmentOperator(assign.OperatorToken.Text),
            _parent.TransformExpression(assign.Right)
        );

    public TsExpression TransformPrefixUnary(PrefixUnaryExpressionSyntax prefix) =>
        new TsUnaryExpression(
            prefix.OperatorToken.Text,
            _parent.TransformExpression(prefix.Operand)
        );

    private static string MapBinaryOperator(string op) => op switch
    {
        "==" => "===",
        "!=" => "!==",
        _ => op
    };

    private static string MapAssignmentOperator(string op) => op switch
    {
        "??=" => "??=",
        _ => op
    };
}
