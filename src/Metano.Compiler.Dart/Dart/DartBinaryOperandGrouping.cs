using Metano.Compiler.IR;

namespace Metano.Compiler.Dart;

/// <summary>
/// Decides when a binary operand that is itself a binary expression must be
/// parenthesized to keep the IR's grouping under Dart's operator precedence
/// (<c>a - (b + c)</c>, <c>(a + b) * c</c>). The IR carries grouping only as
/// tree shape, so the printer has to restore the parentheses the source had.
/// Unary and conditional operands are not covered yet.
/// </summary>
internal static class DartBinaryOperandGrouping
{
    /// <summary>
    /// A tighter-binding left operand never needs parentheses; an equal one
    /// does only under Dart's non-associative equality and relational
    /// operators (<c>(a == b) == c</c>).
    /// </summary>
    public static bool NeedsLeftParentheses(IrBinaryExpression binary)
    {
        if (binary.Left is not IrBinaryExpression inner || binary.Operator.IsAssignment())
            return false;

        var outerPrecedence = Precedence(binary.Operator);
        var innerPrecedence = Precedence(inner.Operator);

        return innerPrecedence < outerPrecedence
            || (innerPrecedence == outerPrecedence && IsNonAssociative(binary.Operator));
    }

    /// <summary>
    /// Binary operators are left-associative, so a right operand of equal or
    /// looser precedence means the source grouped it. A chain of one
    /// associative operator (<c>a &amp;&amp; b &amp;&amp; c</c>,
    /// <c>a ?? b ?? c</c>) means the same either way, and an assignment's
    /// right side binds loosest of all.
    /// </summary>
    public static bool NeedsRightParentheses(IrBinaryExpression binary)
    {
        if (binary.Right is not IrBinaryExpression inner || binary.Operator.IsAssignment())
            return false;

        var outerPrecedence = Precedence(binary.Operator);
        var innerPrecedence = Precedence(inner.Operator);

        return innerPrecedence <= outerPrecedence
            && !IsAssociativeChain(binary.Operator, inner.Operator);
    }

    private static bool IsAssociativeChain(IrBinaryOp outer, IrBinaryOp inner) =>
        inner == outer
        && (outer is IrBinaryOp.LogicalAnd or IrBinaryOp.LogicalOr or IrBinaryOp.NullCoalescing);

    private static bool IsNonAssociative(IrBinaryOp op) =>
        op
            is IrBinaryOp.Equal
                or IrBinaryOp.NotEqual
                or IrBinaryOp.LessThan
                or IrBinaryOp.LessThanOrEqual
                or IrBinaryOp.GreaterThan
                or IrBinaryOp.GreaterThanOrEqual;

    /// <summary>Dart operator precedence; a higher value binds tighter.</summary>
    private static int Precedence(IrBinaryOp op) =>
        op switch
        {
            IrBinaryOp.Multiply or IrBinaryOp.Divide or IrBinaryOp.Modulo => 13,
            IrBinaryOp.Add or IrBinaryOp.Subtract => 12,
            IrBinaryOp.LeftShift or IrBinaryOp.RightShift or IrBinaryOp.UnsignedRightShift => 11,
            IrBinaryOp.BitwiseAnd => 10,
            IrBinaryOp.BitwiseXor => 9,
            IrBinaryOp.BitwiseOr => 8,
            IrBinaryOp.LessThan
            or IrBinaryOp.LessThanOrEqual
            or IrBinaryOp.GreaterThan
            or IrBinaryOp.GreaterThanOrEqual => 7,
            IrBinaryOp.Equal or IrBinaryOp.NotEqual => 6,
            IrBinaryOp.LogicalAnd => 5,
            IrBinaryOp.LogicalOr => 4,
            IrBinaryOp.NullCoalescing => 3,
            // Assignments: both callers exclude an assignment outer operator
            // first, and as an inner operand it binds loosest.
            _ => 1,
        };
}
