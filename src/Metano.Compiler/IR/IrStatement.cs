namespace Metano.Compiler.IR;

/// <summary>
/// A semantic statement in the IR.
/// </summary>
public abstract record IrStatement;

/// <summary>
/// An expression used as a statement.
/// </summary>
public sealed record IrExpressionStatement(IrExpression Expression) : IrStatement;

/// <summary>
/// A <c>return</c> statement.
/// </summary>
public sealed record IrReturnStatement(IrExpression? Value = null) : IrStatement;

/// <summary>
/// A local variable declaration.
/// </summary>
public sealed record IrVariableDeclaration(
    string Name,
    IrTypeRef? Type,
    IrExpression? Initializer,
    bool IsConst = false
) : IrStatement;

/// <summary>
/// An <c>if</c> statement with optional <c>else</c> branch.
/// </summary>
public sealed record IrIfStatement(
    IrExpression Condition,
    IReadOnlyList<IrStatement> Then,
    IReadOnlyList<IrStatement>? Else = null
) : IrStatement;

/// <summary>
/// A <c>switch</c> statement.
/// </summary>
public sealed record IrSwitchStatement(IrExpression Expression, IReadOnlyList<IrSwitchCase> Cases)
    : IrStatement;

/// <summary>
/// A case within a <c>switch</c> statement.
/// </summary>
/// <param name="Labels">Case label expressions; empty for the default case.</param>
/// <param name="Body">Statements in this case.</param>
public sealed record IrSwitchCase(
    IReadOnlyList<IrExpression> Labels,
    IReadOnlyList<IrStatement> Body
);

/// <summary>
/// A <c>throw</c> statement.
/// </summary>
public sealed record IrThrowStatement(IrExpression Expression) : IrStatement;

/// <summary>
/// A <c>foreach</c> / <c>for..of</c> loop.
/// </summary>
public sealed record IrForEachStatement(
    string Variable,
    IrTypeRef? VariableType,
    IrExpression Collection,
    IReadOnlyList<IrStatement> Body
) : IrStatement;

/// <summary>
/// A <c>for</c> loop.
/// </summary>
public sealed record IrForStatement(
    IrStatement? Initializer,
    IrExpression? Condition,
    IrExpression? Increment,
    IReadOnlyList<IrStatement> Body
) : IrStatement;

/// <summary>
/// A <c>while</c> loop.
/// </summary>
public sealed record IrWhileStatement(IrExpression Condition, IReadOnlyList<IrStatement> Body)
    : IrStatement;

/// <summary>
/// A <c>do..while</c> loop.
/// </summary>
public sealed record IrDoWhileStatement(IReadOnlyList<IrStatement> Body, IrExpression Condition)
    : IrStatement;

/// <summary>
/// A <c>try..catch..finally</c> statement.
/// </summary>
public sealed record IrTryStatement(
    IReadOnlyList<IrStatement> Body,
    IReadOnlyList<IrCatchClause>? Catches = null,
    IReadOnlyList<IrStatement>? Finally = null
) : IrStatement;

/// <summary>
/// A catch clause in a try statement.
/// </summary>
/// <param name="ExceptionType">The caught exception type, if any.</param>
/// <param name="VariableName">The exception variable name, if any.</param>
/// <param name="Body">Catch body statements.</param>
public sealed record IrCatchClause(
    IrTypeRef? ExceptionType,
    string? VariableName,
    IReadOnlyList<IrStatement> Body
);

/// <summary>
/// A <c>break</c> statement.
/// </summary>
public sealed record IrBreakStatement() : IrStatement;

/// <summary>
/// A <c>continue</c> statement.
/// </summary>
public sealed record IrContinueStatement() : IrStatement;

/// <summary>
/// A <c>yield break</c> statement (terminates an iterator).
/// </summary>
public sealed record IrYieldBreakStatement() : IrStatement;

/// <summary>
/// A block of statements (scoped).
/// </summary>
public sealed record IrBlockStatement(IReadOnlyList<IrStatement> Statements) : IrStatement;
