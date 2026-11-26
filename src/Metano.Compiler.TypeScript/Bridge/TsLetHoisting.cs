using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Hoists the bindings of a statement's own value out of it (W13-1, #804): when
/// the expression of an expression statement, the value it assigns to a local or
/// a member of <c>this</c>, the initializer of a declaration, or the value of a
/// <c>return</c>, <c>throw</c> or <c>yield</c> is an
/// <see cref="IrLetExpression"/> (an object or collection initializer, a
/// receiver evaluated once), the binding becomes a <c>const</c> declared right
/// before the statement and each effect of its body a statement of its own, so
/// <c>return new Section() { new H2() };</c> lowers to
/// <c>const t = new Section(); t.add(new H2()); return t;</c>. The order is the
/// C# one: the value, then each effect, then the statement over the result. A
/// binding in any other position stays an expression over a temporary
/// (<see cref="TsTemporaries"/>). A block-scoped <c>const</c> is a fresh binding
/// per execution, so a closure may read it.
/// </summary>
internal static class TsLetHoisting
{
    /// <summary>
    /// The statements <paramref name="statement"/> splits into, in order, or
    /// <c>null</c> when its value is not a binding it can hoist.
    /// </summary>
    public static IReadOnlyList<IrStatement>? TryHoist(
        IrStatement statement,
        IrToTsBridgeContext context
    )
    {
        if (ValueOf(statement) is not IrLetExpression let)
            return null;

        var (prelude, result) = Split(let, context);

        if (prelude.Count == 0)
            return null;

        var residual = WithValue(statement, result);

        IEnumerable<IrStatement> statements = residual is null ? prelude : [.. prelude, residual];

        return [.. statements.Select(hoisted => hoisted with { Span = statement.Span })];
    }

    /// <summary>
    /// The statements that evaluate <paramref name="value"/>'s leading bindings and
    /// effects, in order, and the expression left to yield its value: the value
    /// itself when it starts with no binding this list can hoist.
    /// </summary>
    public static (IReadOnlyList<IrStatement> Prelude, IrExpression Value) Split(
        IrExpression value,
        IrToTsBridgeContext context
    )
    {
        var prelude = new List<IrStatement>();
        var rest = value;

        while (rest is IrLetExpression let && TryClaimBinding(let, context))
        {
            prelude.Add(new IrVariableDeclaration(let.Name, Type: null, let.Value, IsConst: true));

            if (let.Body is IrSequenceExpression sequence)
            {
                prelude.AddRange(
                    sequence.Effects.Select(effect => new IrExpressionStatement(effect))
                );

                rest = sequence.Result;
            }
            else
            {
                rest = let.Body;
            }
        }

        return (prelude, rest);
    }

    /// <summary>
    /// Claims the binding's name for a declaration of the statement list of
    /// <paramref name="context"/>, and whether it did: not for a binding that lowers
    /// binding nothing (<see cref="IrToTsExpressionBridge.LowersWithoutBinding"/>),
    /// nor when a temporary of the list holds the name.
    /// </summary>
    private static bool TryClaimBinding(IrLetExpression let, IrToTsBridgeContext context) =>
        context.Temporaries is { } temporaries
        && !IrToTsExpressionBridge.LowersWithoutBinding(let, context)
        && temporaries.TryClaimExactly(IrToTsNamingPolicy.ToLocalName(let.Name));

    private static IrExpression? ValueOf(IrStatement statement) =>
        PlainStoreOf(statement)?.Right
        ?? statement switch
        {
            IrExpressionStatement { Expression: IrYieldExpression { Value: { } yielded } } =>
                yielded,
            IrExpressionStatement expression => expression.Expression,
            IrReturnStatement { Value: { } returned } => returned,
            IrThrowStatement thrown => thrown.Expression,
            IrVariableDeclaration { Initializer: { } initializer } => initializer,
            _ => null,
        };

    /// <summary>
    /// The expression of <paramref name="statement"/> when it is a plain assignment
    /// to a local or to a member of <c>this</c>: evaluating its target reads
    /// nothing, so its value may run first, as a statement of its own.
    /// </summary>
    private static IrBinaryExpression? PlainStoreOf(IrStatement statement) =>
        statement
            is IrExpressionStatement
            {
                Expression: IrBinaryExpression
                {
                    Operator: IrBinaryOp.Assign,
                    Left: IrIdentifier or IrMemberAccess { Target: IrThisExpression },
                } store,
            }
            ? store
            : null;

    /// <summary>
    /// <paramref name="statement"/> over <paramref name="value"/> in place of its
    /// own; <c>null</c> for an expression statement whose remaining value is a
    /// bare read, which has no effect left.
    /// </summary>
    private static IrStatement? WithValue(IrStatement statement, IrExpression value) =>
        PlainStoreOf(statement) is { } store
            ? new IrExpressionStatement(store with { Right = value })
            : WithValueOfItsKind(statement, value);

    private static IrStatement? WithValueOfItsKind(IrStatement statement, IrExpression value) =>
        statement switch
        {
            IrExpressionStatement { Expression: IrYieldExpression yield } =>
                new IrExpressionStatement(yield with { Value = value }),
            IrExpressionStatement when value is IrIdentifier => null,
            IrExpressionStatement => new IrExpressionStatement(value),
            IrReturnStatement => new IrReturnStatement(value),
            IrThrowStatement => new IrThrowStatement(value),
            IrVariableDeclaration declaration => declaration with { Initializer = value },
            _ => throw new InvalidOperationException(
                $"No value to replace in {statement.GetType().Name}."
            ),
        };
}
