using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Parenthesization.Templates;

namespace Metano.Compiler.TypeScript.Parenthesization;

/// <summary>
/// Maps a TypeScript AST expression (or a binary operator token) to the
/// <see cref="TsPrecedence"/> it binds with when printed bare. Explicit
/// <see cref="TsParenthesized"/> grouping is a primary atom, so a node a
/// bridge already wrapped is never wrapped twice. A <see cref="TsTemplate"/>
/// is classified from its template text (<see cref="TsTemplateShape"/>);
/// an unrecognized node or token is <see cref="TsPrecedence.Lowest"/>.
/// </summary>
internal static class TsPrecedenceClassifier
{
    public static TsPrecedence Of(TsExpression expression) =>
        expression switch
        {
            TsLiteral literal => OfLiteral(literal),
            TsIdentifier
            or TsTypeReference
            or TsStringLiteral
            or TsTemplateLiteral
            or TsArrayLiteral
            or TsObjectLiteral
            or TsNamedFunctionExpression
            or TsJsxElement
            or TsParenthesized => TsPrecedence.Primary,
            TsPropertyAccess
            or TsElementAccess
            or TsCallExpression
            or TsNewExpression
            or TsLinqCall => TsPrecedence.Member,
            TsPostfixUnaryExpression => TsPrecedence.Postfix,
            TsUnaryExpression or TsAwaitExpression => TsPrecedence.Unary,
            TsBinaryExpression binary => OfOperator(binary.Operator),
            TsCastExpression => TsPrecedence.Relational,
            TsConditionalExpression => TsPrecedence.Conditional,
            TsArrowFunction or TsSpreadExpression => TsPrecedence.Assignment,
            TsTemplate template => OfTemplate(template),
            _ => TsPrecedence.Lowest,
        };

    public static TsPrecedence OfOperator(string token) =>
        token switch
        {
            "**" => TsPrecedence.Exponent,
            "*" or "/" or "%" => TsPrecedence.Multiplicative,
            "+" or "-" => TsPrecedence.Additive,
            "<<" or ">>" or ">>>" => TsPrecedence.Shift,
            "<" or ">" or "<=" or ">=" or "instanceof" or "in" => TsPrecedence.Relational,
            "==" or "!=" or "===" or "!==" => TsPrecedence.Equality,
            "&" => TsPrecedence.BitwiseAnd,
            "^" => TsPrecedence.BitwiseXor,
            "|" => TsPrecedence.BitwiseOr,
            "&&" => TsPrecedence.LogicalAnd,
            "||" or "??" => TsPrecedence.ShortCircuit,
            _ when IsAssignmentOperator(token) => TsPrecedence.Assignment,
            _ => TsPrecedence.Lowest,
        };

    public static bool IsAssignmentOperator(string token) =>
        token
            is "="
                or "+="
                or "-="
                or "*="
                or "/="
                or "%="
                or "**="
                or "<<="
                or ">>="
                or ">>>="
                or "&="
                or "|="
                or "^="
                or "&&="
                or "||="
                or "??=";

    /// <summary>
    /// Constant literals reach the AST as raw text, so a negative number
    /// (<c>-1</c>) is really a unary minus applied to a literal.
    /// </summary>
    private static TsPrecedence OfLiteral(TsLiteral literal) =>
        IsSignedLiteral(literal) ? TsPrecedence.Unary : TsPrecedence.Primary;

    private static bool IsSignedLiteral(TsLiteral literal) =>
        literal.Raw.StartsWith('-') || literal.Raw.StartsWith('+');

    /// <summary>
    /// A template that is nothing but one value placeholder
    /// (<c>$this</c>, <c>$0</c>) is transparent: it binds exactly like the
    /// node substituted into it.
    /// </summary>
    private static TsPrecedence OfTemplate(TsTemplate template) =>
        TsTemplateShape.TransparentSubstitution(template) is { } substituted
            ? Of(substituted)
            : TsTemplateShape.Of(template).TopLevelOperator.Precedence;
}
