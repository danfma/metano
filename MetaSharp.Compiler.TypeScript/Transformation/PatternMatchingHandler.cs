using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Handles C# pattern-matching forms (<c>is</c> patterns and <c>switch</c>-expression arms)
/// and lowers them into the equivalent TypeScript boolean conditions.
///
/// Patterns supported:
/// <list type="bullet">
///   <item><c>x is null</c> / <c>x is "lit"</c> / <c>x is 42</c> → <c>=== </c></item>
///   <item><c>x is not P</c> → <c>!(P)</c></item>
///   <item><c>x is Type t</c> / <c>x is Type</c> → <c>instanceof Type</c> (or <c>typeof === "..."</c> for primitives)</item>
///   <item><c>x is &gt; 0</c>, <c>x is &gt;= 0 and &lt; 100</c> → relational/binary patterns</item>
///   <item><c>x is { Prop: value }</c> → property pattern (recurses on each subpattern)</item>
///   <item><c>_</c> (discard) → <c>true</c></item>
/// </list>
///
/// Holds a reference to the parent <see cref="ExpressionTransformer"/> for two reasons:
/// recursive expression transformation (constant patterns embed expressions) and access
/// to the shared <see cref="SemanticModel"/> for type-pattern lookups.
/// </summary>
public sealed class PatternMatchingHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression TransformIsPattern(IsPatternExpressionSyntax isPattern)
    {
        var expr = _parent.TransformExpression(isPattern.Expression);
        return TransformPatternToCondition(expr, isPattern.Pattern);
    }

    public TsExpression TransformPatternToCondition(TsExpression expr, PatternSyntax pattern)
    {
        return pattern switch
        {
            // x is null → x === null
            ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal }
                when literal.IsKind(SyntaxKind.NullLiteralExpression) =>
                new TsBinaryExpression(expr, "===", new TsLiteral("null")),

            // x is "value" or x is 42
            ConstantPatternSyntax constant =>
                new TsBinaryExpression(expr, "===", _parent.TransformExpression(constant.Expression)),

            // x is not pattern → !(condition)
            UnaryPatternSyntax { OperatorToken.Text: "not" } unary =>
                new TsUnaryExpression("!", new TsParenthesized(TransformPatternToCondition(expr, unary.Pattern))),

            // x is Type → x instanceof Type
            DeclarationPatternSyntax declaration =>
                TransformTypePattern(expr, declaration.Type),

            // x is Type (without variable)
            TypePatternSyntax typePattern =>
                TransformTypePattern(expr, typePattern.Type),

            // x is > 0
            RelationalPatternSyntax relational =>
                new TsBinaryExpression(expr, MapBinaryOperator(relational.OperatorToken.Text),
                    _parent.TransformExpression(relational.Expression)),

            // x is >= 0 and < 100
            BinaryPatternSyntax binary =>
                new TsBinaryExpression(
                    TransformPatternToCondition(expr, binary.Left),
                    binary.OperatorToken.Text == "and" ? "&&" : "||",
                    TransformPatternToCondition(expr, binary.Right)
                ),

            // x is { Prop: value }
            RecursivePatternSyntax recursive when recursive.PropertyPatternClause is not null =>
                TransformPropertyPattern(expr, recursive),

            // Discard _
            DiscardPatternSyntax => new TsLiteral("true"),

            _ => new TsLiteral($"true /* unsupported pattern: {pattern.Kind()} */")
        };
    }

    private TsExpression TransformTypePattern(TsExpression expr, TypeSyntax typeSyntax)
    {
        var typeInfo = _parent.Model.GetTypeInfo(typeSyntax);
        var type = typeInfo.Type;

        if (type is null)
            return new TsBinaryExpression(expr, "instanceof", new TsIdentifier(typeSyntax.ToString()));

        // Primitive type checks → typeof
        return type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double
                or SpecialType.System_Single or SpecialType.System_Decimal =>
                new TsBinaryExpression(new TsUnaryExpression("typeof ", expr), "===",
                    new TsStringLiteral("number")),

            SpecialType.System_String =>
                new TsBinaryExpression(new TsUnaryExpression("typeof ", expr), "===",
                    new TsStringLiteral("string")),

            SpecialType.System_Boolean =>
                new TsBinaryExpression(new TsUnaryExpression("typeof ", expr), "===",
                    new TsStringLiteral("boolean")),

            // Class/struct → instanceof
            _ => new TsBinaryExpression(expr, "instanceof", new TsIdentifier(type.Name))
        };
    }

    private TsExpression TransformPropertyPattern(TsExpression expr, RecursivePatternSyntax recursive)
    {
        TsExpression? result = null;

        foreach (var subpattern in recursive.PropertyPatternClause!.Subpatterns)
        {
            var propName = TypeScriptNaming.ToCamelCase(subpattern.NameColon!.Name.Identifier.Text);
            var propAccess = new TsPropertyAccess(expr, propName);
            var condition = TransformPatternToCondition(propAccess, subpattern.Pattern);

            result = result is null ? condition : new TsBinaryExpression(result, "&&", condition);
        }

        // Add type check if recursive pattern has a type
        if (recursive.Type is not null)
        {
            var typeCheck = TransformTypePattern(expr, recursive.Type);
            result = result is null ? typeCheck : new TsBinaryExpression(typeCheck, "&&", result);
        }

        return result ?? new TsLiteral("true");
    }

    private static string MapBinaryOperator(string op) => op switch
    {
        "==" => "===",
        "!=" => "!==",
        _ => op
    };
}
