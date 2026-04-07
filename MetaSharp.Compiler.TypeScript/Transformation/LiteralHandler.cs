using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Lowers C# literal expressions (<c>"text"</c>, <c>42</c>, <c>true</c>, <c>null</c>,
/// <c>default</c>) into the corresponding TypeScript literal AST nodes.
///
/// Notes on individual mappings:
/// <list type="bullet">
///   <item><c>null</c> and <c>default</c> both lower to <c>null</c> — see the user's
///   preference recorded in memory for keeping <c>T | null = null</c> instead of
///   converting to <c>T?</c>.</item>
///   <item>Numeric literals are forwarded by their token's <c>ValueText</c>, which already
///   strips the C# numeric suffixes (<c>m</c>, <c>L</c>, <c>f</c>, <c>d</c>).</item>
/// </list>
///
/// Pure / stateless: takes only the syntax node.
/// </summary>
public static class LiteralHandler
{
    public static TsExpression Transform(LiteralExpressionSyntax lit)
    {
        return lit.Kind() switch
        {
            SyntaxKind.StringLiteralExpression => new TsStringLiteral(lit.Token.ValueText),
            SyntaxKind.TrueLiteralExpression => new TsLiteral("true"),
            SyntaxKind.FalseLiteralExpression => new TsLiteral("false"),
            SyntaxKind.NullLiteralExpression => new TsLiteral("null"),
            SyntaxKind.DefaultLiteralExpression => new TsLiteral("null"),
            // Numeric: strip suffixes (m, L, f, d)
            _ => new TsLiteral(lit.Token.ValueText),
        };
    }
}
