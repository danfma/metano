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
/// <param name="InferredType">The compiler-inferred type of a <c>var</c> local,
/// whose <see cref="Type"/> stays <c>null</c>; used when a backend must
/// declare the local ahead of its initializer.</param>
/// <param name="AssignedInWhenGuard">The declaration has no initializer and the
/// local is assigned by an <c>is</c> pattern in a <c>when</c> guard of a
/// <c>switch</c> section that reads it. A backend that runs guards outside the
/// section's control flow (as a closure) must assert the local is definitely
/// assigned before the body reads it.</param>
public sealed record IrVariableDeclaration(
    string Name,
    IrTypeRef? Type,
    IrExpression? Initializer,
    bool IsConst = false,
    bool AssignedInWhenGuard = false,
    IrTypeRef? InferredType = null
) : IrStatement;

/// <summary>
/// A deconstructing variable declaration — <c>var (a, b) = expr;</c>. Lowers to
/// a JS array-destructuring binding (<c>const [a, b] = expr</c>). Each element is
/// a bound name or a discard (<see cref="IrDeconstructionElement.Name"/> null).
/// Target-agnostic: a Dart target could consume the same shape as record-pattern
/// destructuring.
/// </summary>
/// <param name="Elements">The positional binding slots, in declaration order.</param>
/// <param name="Initializer">The right-hand side that produces the tuple value.</param>
/// <param name="IsConst">True when no bound local is reassigned (mirrors
/// <see cref="IrVariableDeclaration.IsConst"/>); false promotes the binding to <c>let</c>.</param>
public sealed record IrTupleDeconstruction(
    IReadOnlyList<IrDeconstructionElement> Elements,
    IrExpression Initializer,
    bool IsConst = true
) : IrStatement;

/// <summary>
/// One slot of an <see cref="IrTupleDeconstruction"/>. A null
/// <paramref name="Name"/> denotes a discard (<c>_</c>) — an empty hole in the
/// emitted array pattern that binds nothing.
/// </summary>
/// <param name="Name">The bound local name, or null for a discard.</param>
/// <param name="Type">The optional declared element type (usually null for <c>var</c>).</param>
public sealed record IrDeconstructionElement(string? Name, IrTypeRef? Type = null);

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
/// A section of a <c>switch</c> statement: one or more labels sharing a body.
/// </summary>
/// <param name="Labels">The section's labels in source order — constants,
/// patterns (with their <c>when</c> guards) and <c>default</c>, which may sit
/// anywhere among them.</param>
/// <param name="Body">Statements in this section.</param>
public sealed record IrSwitchCase(
    IReadOnlyList<IrCaseLabel> Labels,
    IReadOnlyList<IrStatement> Body
);

/// <summary>
/// A <c>throw</c> statement.
/// </summary>
public sealed record IrThrowStatement(IrExpression Expression) : IrStatement;

/// <summary>
/// A <c>throw;</c> statement: rethrows the exception the innermost enclosing
/// catch clause caught, whatever the clause's variable holds by then.
/// </summary>
public sealed record IrRethrowStatement() : IrStatement;

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
/// <param name="Initializers">The header's initializer section, in source
/// order: one <see cref="IrVariableDeclaration"/> per declarator of
/// <c>for (int i = 0, j = n; …)</c>, or one <see cref="IrExpressionStatement"/>
/// per expression of <c>for (i = 0, j = n; …)</c>. Empty when the section is.
/// A declaration is per-iteration, as in C#: a closure made in the body keeps
/// the value its own iteration saw, so a backend must not share one variable
/// across iterations.</param>
/// <param name="Condition">The loop condition, or null when omitted.</param>
/// <param name="Incrementors">The header's iterator expressions, in source
/// order (<c>i++, j--</c>). Empty when the section is.</param>
/// <param name="Body">The loop body.</param>
public sealed record IrForStatement(
    IReadOnlyList<IrStatement> Initializers,
    IrExpression? Condition,
    IReadOnlyList<IrExpression> Incrementors,
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
/// <param name="Filter">The <c>when (…)</c> exception filter, if any: the
/// clause handles the exception only when it holds, and the next clause is
/// tried otherwise. It may read <paramref name="VariableName"/>.</param>
public sealed record IrCatchClause(
    IrTypeRef? ExceptionType,
    string? VariableName,
    IReadOnlyList<IrStatement> Body,
    IrExpression? Filter = null
)
{
    /// <summary>
    /// Whether the clause's type accepts every exception: a general
    /// <c>catch</c>, or <c>catch (System.Exception)</c>.
    /// </summary>
    public bool CatchesEveryException =>
        ExceptionType is null or IrNamedTypeRef { Name: "Exception", Namespace: "System" };

    /// <summary>
    /// Whether the clause handles every exception that reaches it: it accepts
    /// every exception and has no filter, so no later clause is ever tried.
    /// </summary>
    public bool HandlesEveryException => CatchesEveryException && Filter is null;
}

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
