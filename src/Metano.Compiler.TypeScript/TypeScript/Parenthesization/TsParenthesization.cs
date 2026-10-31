using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Parenthesization.Templates;

namespace Metano.Compiler.TypeScript.Parenthesization;

/// <summary>
/// Decides whether an operand must be wrapped in parentheses to keep the
/// tree's grouping when printed in a given <see cref="TsOperandSlot"/>.
/// The rules are grammar-minimal: they add parentheses only where the
/// ECMAScript/TypeScript grammar would otherwise regroup or reject the
/// text. Some rules are not about binding strength:
/// <list type="bullet">
///   <item>TS5076 — <c>??</c> cannot be mixed with an unparenthesized
///   <c>||</c>/<c>&amp;&amp;</c> operand, in either direction;</item>
///   <item>token gluing — <c>-(-a)</c> and <c>-(--b)</c> printed bare
///   would lex as a decrement (<c>--a</c>, <c>---b</c>);</item>
///   <item>a cast's trailing type swallows a following <c>|</c>/<c>&amp;</c>
///   and reads <c>&lt;</c> as type arguments;</item>
///   <item>an optional chain used as a member object is closed
///   (<c>(a?.b).c</c>).</item>
/// </list>
/// </summary>
internal static class TsParenthesization
{
    public static bool NeedsParens(TsExpression operand, TsOperandSlot slot)
    {
        var precedence = TsPrecedenceClassifier.Of(operand);

        return slot.Kind switch
        {
            TsSlotKind.BinaryLeft => NeedsParensAsBinaryOperand(
                operand,
                precedence,
                slot.RequiredOperator,
                isLeftOperand: true
            ),
            TsSlotKind.BinaryRight => NeedsParensAsBinaryOperand(
                operand,
                precedence,
                slot.RequiredOperator,
                isLeftOperand: false
            ),
            TsSlotKind.Prefix => precedence < TsPrecedence.Unary
                || GluesWithPrefix(operand, slot.RequiredOperator),
            TsSlotKind.Postfix => precedence < TsPrecedence.Member,
            TsSlotKind.CastOperand => precedence < TsPrecedence.Relational,
            TsSlotKind.AccessTarget => NeedsParensAsAccessTarget(operand, precedence),
            TsSlotKind.MemberObject => NeedsParensAsAccessTarget(operand, precedence)
                || EndsWithOptionalLink(operand),
            TsSlotKind.NewCallee => precedence < TsPrecedence.Member || ChainContainsCall(operand),
            TsSlotKind.ConditionalTest => precedence < TsPrecedence.ShortCircuit,
            TsSlotKind.AssignmentExpression => precedence < TsPrecedence.Assignment,
            TsSlotKind.ConciseArrowBody => precedence < TsPrecedence.Assignment
                || StartsWithObjectLiteral(operand),
            TsSlotKind.ExpressionStatement => StartsLikeADeclaration(operand),
            _ => true,
        };
    }

    public static bool NeedsParensInAny(TsExpression operand, IReadOnlyList<TsOperandSlot> slots) =>
        slots.Any(slot => NeedsParens(operand, slot));

    private static bool NeedsParensAsBinaryOperand(
        TsExpression operand,
        TsPrecedence precedence,
        string op,
        bool isLeftOperand
    )
    {
        if (IsShortCircuitMix(op, operand))
            return true;

        if (isLeftOperand && IsTypeContinuation(op) && EndsWithTypeAnnotation(operand))
            return true;

        if (TsPrecedenceClassifier.IsAssignmentOperator(op))
            return precedence < (isLeftOperand ? TsPrecedence.Member : TsPrecedence.Assignment);

        return op switch
        {
            // `-a ** b` is a syntax error, so the base must be a postfix
            // expression or tighter; `**` is right-associative.
            "**" => precedence < (isLeftOperand ? TsPrecedence.Postfix : TsPrecedence.Exponent),
            // Short-circuit operators are associative in both value and
            // evaluation order, so a same-operator chain stays flat on either
            // side (`a ?? b ?? c`, `f && x > 0 && x < 10`).
            "&&" or "||" or "??" => precedence < TsPrecedenceClassifier.OfOperator(op),
            _ => NeedsParensForLeftAssociative(precedence, op, isLeftOperand),
        };
    }

    private static bool NeedsParensForLeftAssociative(
        TsPrecedence precedence,
        string op,
        bool isLeftOperand
    )
    {
        var operatorPrecedence = TsPrecedenceClassifier.OfOperator(op);

        // Unknown operator: conservatively wrap every non-atomic operand.
        if (operatorPrecedence is TsPrecedence.Lowest)
            return precedence < TsPrecedence.Primary;

        return isLeftOperand ? precedence < operatorPrecedence : precedence <= operatorPrecedence;
    }

    private static bool NeedsParensAsAccessTarget(TsExpression operand, TsPrecedence precedence) =>
        precedence < TsPrecedence.Member || IsUnsignedNumericLiteral(operand);

    /// <summary>
    /// TS5076: <c>a ?? b || c</c> and <c>a || b ?? c</c> are syntax errors,
    /// so a <c>||</c>/<c>&amp;&amp;</c> operand of <c>??</c> (and the
    /// reverse) is always parenthesized.
    /// </summary>
    private static bool IsShortCircuitMix(string op, TsExpression operand) =>
        TopLevelOperatorToken(operand) is { } operandOp
        && (IsNullishOverLogical(op, operandOp) || IsNullishOverLogical(operandOp, op));

    private static bool IsNullishOverLogical(string outer, string inner) =>
        outer is "??" && inner is "||" or "&&";

    /// <summary>
    /// The loosest binary operator token the operand prints bare at its top
    /// level, seen through templates.
    /// </summary>
    private static string? TopLevelOperatorToken(TsExpression operand) =>
        operand switch
        {
            TsBinaryExpression binary => binary.Operator,
            TsTemplate template => TsTemplateShape.TransparentSubstitution(template) is { } inner
                ? TopLevelOperatorToken(inner)
                : TsTemplateShape.Of(template).TopLevelOperator.Token,
            _ => null,
        };

    /// <summary>
    /// After a cast's type, <c>|</c> and <c>&amp;</c> continue the type
    /// (<c>x as A | b</c> is <c>x as (A | b)</c>) and <c>&lt;</c> opens type
    /// arguments.
    /// </summary>
    private static bool IsTypeContinuation(string op) => op is "|" or "&" or "<";

    /// <summary>
    /// Whether the operand's text ends with a cast's type: a rightmost walk,
    /// since <c>a === x as T</c> ends with <c>T</c> too.
    /// </summary>
    private static bool EndsWithTypeAnnotation(TsExpression operand) =>
        operand switch
        {
            TsCastExpression => true,
            TsBinaryExpression binary => EndsWithTypeAnnotation(binary.Right),
            TsConditionalExpression conditional => EndsWithTypeAnnotation(conditional.WhenFalse),
            TsTemplate template
                when TsTemplateShape.TransparentSubstitution(template) is { } inner =>
                EndsWithTypeAnnotation(inner),
            TsTemplate template => TopLevelOperatorToken(template) is "as" or "satisfies",
            _ => false,
        };

    /// <summary>
    /// A prefix <c>-</c>/<c>+</c> followed by an operand that itself
    /// starts with the same sign would lex as a different token
    /// (<c>--a</c>, <c>---b</c>, <c>++a</c>).
    /// </summary>
    private static bool GluesWithPrefix(TsExpression operand, string op) =>
        op is "-" or "+" && StartsWithSign(operand, op[0]);

    private static bool StartsWithSign(TsExpression operand, char sign) =>
        operand switch
        {
            TsUnaryExpression unary => unary.Operator.StartsWith(sign),
            TsLiteral literal => literal.Raw.StartsWith(sign),
            TsTemplate template => TemplateStartsWithSign(template, sign),
            _ => false,
        };

    private static bool TemplateStartsWithSign(TsTemplate template, char sign) =>
        TsTemplateShape.TransparentSubstitution(template) is { } inner
            ? StartsWithSign(inner, sign)
            : template.Template.TrimStart().StartsWith(sign);

    /// <summary>
    /// <c>1.toString()</c> is a syntax error (TS1351): the dot is read as
    /// the decimal point. A signed literal already binds as unary and is
    /// wrapped by the precedence rule.
    /// </summary>
    private static bool IsUnsignedNumericLiteral(TsExpression operand) =>
        operand switch
        {
            TsLiteral { Raw: [var first, ..] } => char.IsAsciiDigit(first) || first == '.',
            TsTemplate template
                when TsTemplateShape.TransparentSubstitution(template) is { } inner =>
                IsUnsignedNumericLiteral(inner),
            _ => false,
        };

    /// <summary>
    /// Whether the operand's own access chain ends in an optional link
    /// (<c>a?.b</c>, <c>f?.()</c>, <c>a?.b[0]</c>): continuing it with a
    /// plain member access would extend the short-circuit.
    /// </summary>
    private static bool EndsWithOptionalLink(TsExpression operand) =>
        operand switch
        {
            TsPropertyAccess access => access.Optional || EndsWithOptionalLink(access.Object),
            TsElementAccess element => element.Optional || EndsWithOptionalLink(element.Object),
            TsCallExpression call => call.Optional || EndsWithOptionalLink(call.Callee),
            TsTemplate template
                when TsTemplateShape.TransparentSubstitution(template) is { } inner =>
                EndsWithOptionalLink(inner),
            _ => false,
        };

    /// <summary>
    /// <c>new f()()</c> calls the result of <c>new f()</c>; a callee whose
    /// member chain contains a call must be grouped: <c>new (f())()</c>.
    /// </summary>
    private static bool ChainContainsCall(TsExpression operand) =>
        operand switch
        {
            TsCallExpression or TsLinqCall or TsTemplate => true,
            TsPropertyAccess access => access.Optional || ChainContainsCall(access.Object),
            TsElementAccess element => element.Optional || ChainContainsCall(element.Object),
            _ => false,
        };

    /// <summary>
    /// A statement that starts with <c>{</c> is a block, and one that starts
    /// with <c>function</c> is a declaration.
    /// </summary>
    private static bool StartsLikeADeclaration(TsExpression operand) =>
        StartsWithObjectLiteral(operand) || Leftmost(operand) is TsNamedFunctionExpression;

    /// <summary>
    /// An object literal at the start of a statement or of a concise arrow
    /// body is read as a block.
    /// </summary>
    private static bool StartsWithObjectLiteral(TsExpression operand) =>
        Leftmost(operand) switch
        {
            TsObjectLiteral => true,
            TsTemplate template => TsTemplateShape.Of(template).IsObjectLiteral,
            _ => false,
        };

    /// <summary>
    /// The node whose text is printed first, seen through a template's
    /// leading placeholder. Over-approximates when an inner operand is
    /// parenthesized by its own slot — harmless, it only adds a redundant
    /// pair of parentheses.
    /// </summary>
    private static TsExpression Leftmost(TsExpression expression) =>
        expression switch
        {
            TsBinaryExpression binary => Leftmost(binary.Left),
            TsPropertyAccess access => Leftmost(access.Object),
            TsElementAccess element => Leftmost(element.Object),
            TsCallExpression call => Leftmost(call.Callee),
            TsPostfixUnaryExpression postfix => Leftmost(postfix.Operand),
            TsConditionalExpression conditional => Leftmost(conditional.Condition),
            TsCastExpression cast => Leftmost(cast.Expression),
            TsTemplate template when TsTemplateShape.LeadingSubstitution(template) is { } leading =>
                Leftmost(leading),
            _ => expression,
        };
}
