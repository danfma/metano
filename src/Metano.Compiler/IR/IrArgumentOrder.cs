namespace Metano.Compiler.IR;

/// <summary>
/// Keeps an argument evaluation order (<see cref="IrCallExpression.EvaluationOrder"/>:
/// positions into an argument list, in the order the source evaluates them)
/// aligned with a list that loses or regains arguments.
/// </summary>
public static class IrArgumentOrder
{
    /// <summary>
    /// <paramref name="order"/> over the list without the arguments at
    /// <paramref name="removed"/>: their positions leave it and the later
    /// ones shift down. <c>null</c> when what remains is parameter order.
    /// </summary>
    public static IReadOnlyList<int>? Without(IReadOnlyList<int>? order, IReadOnlyList<int> removed)
    {
        if (order is null)
            return null;

        var remaining = order
            .Where(position => !removed.Contains(position))
            .Select(position => position - removed.Count(gone => gone < position))
            .ToList();

        return remaining.SequenceEqual(remaining.Order()) ? null : remaining;
    }

    /// <summary>
    /// <paramref name="order"/> over the list with one argument inserted at
    /// <paramref name="inserted"/>: the positions from there on shift up and
    /// the new argument, a value no order tells apart, is evaluated last.
    /// </summary>
    public static IReadOnlyList<int>? WithInsertion(IReadOnlyList<int>? order, int inserted) =>
        order is null
            ? null
            :
            [
                .. order.Select(position => position >= inserted ? position + 1 : position),
                inserted,
            ];
}
