namespace Metano.Compiler.TypeScript.Parenthesization;

/// <summary>
/// Binding strength of a TypeScript expression, from the loosest
/// (<see cref="Lowest"/>) to the tightest (<see cref="Primary"/>). The
/// levels follow the ECMAScript grammar (MDN operator-precedence table);
/// TS <c>as</c> rides the relational level. <see cref="Lowest"/> is the
/// conservative answer for an unknown operator or node: it over-wraps
/// rather than miscompiles. <see cref="Comma"/> is the sequence
/// expression, a <c>TsBinaryExpression</c> with the <c>,</c> operator.
/// </summary>
internal enum TsPrecedence
{
    Lowest = 0,
    Comma = 1,
    Assignment = 2,
    Conditional = 3,
    ShortCircuit = 4,
    LogicalAnd = 5,
    BitwiseOr = 6,
    BitwiseXor = 7,
    BitwiseAnd = 8,
    Equality = 9,
    Relational = 10,
    Shift = 11,
    Additive = 12,
    Multiplicative = 13,
    Exponent = 14,
    Unary = 15,
    Postfix = 16,
    Member = 17,
    Primary = 18,
}
