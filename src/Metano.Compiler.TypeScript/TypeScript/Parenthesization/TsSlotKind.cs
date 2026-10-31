namespace Metano.Compiler.TypeScript.Parenthesization;

/// <summary>
/// The grammatical position an operand is printed in. Together with the
/// operand's own <see cref="TsPrecedence"/> it decides whether the
/// printer has to wrap the operand in parentheses.
/// </summary>
internal enum TsSlotKind
{
    /// <summary>Left operand of a binary or assignment operator.</summary>
    BinaryLeft,

    /// <summary>Right operand of a binary or assignment operator.</summary>
    BinaryRight,

    /// <summary>Operand of a prefix operator (<c>-</c>, <c>!</c>, <c>typeof</c>, <c>await</c>).</summary>
    Prefix,

    /// <summary>Operand of a postfix <c>++</c>/<c>--</c>.</summary>
    Postfix,

    /// <summary>Left side of a TS <c>as</c> cast.</summary>
    CastOperand,

    /// <summary>
    /// Callee of a call, or any position that needs a member-level
    /// expression (the conservative default for unknown template neighbors).
    /// </summary>
    AccessTarget,

    /// <summary>
    /// Object of a non-optional property or element access. Like
    /// <see cref="AccessTarget"/>, and an optional chain is closed with
    /// parentheses: <c>(a?.b).c</c> must throw on a null <c>a</c> the way
    /// C# does, where <c>a?.b.c</c> would short-circuit.
    /// </summary>
    MemberObject,

    /// <summary>Callee of a <c>new</c> expression.</summary>
    NewCallee,

    /// <summary>Test of a conditional (<c>test ? a : b</c>).</summary>
    ConditionalTest,

    /// <summary>
    /// Any position whose grammar is an <c>AssignmentExpression</c>:
    /// conditional branches, spread operands, object values.
    /// </summary>
    AssignmentExpression,

    /// <summary>Expression body of a concise arrow function.</summary>
    ConciseArrowBody,

    /// <summary>The expression of an expression statement.</summary>
    ExpressionStatement,
}
