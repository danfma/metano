using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Collects the names of locals an IR fragment rebinds: every
/// <see cref="IrIdentifier"/> that is the target of an assignment (simple or
/// compound) or a prefix/postfix increment / decrement, including inside
/// nested lambdas. <see cref="IrMemberAccess"/> and indexer writes do not
/// count — they do not rebind a local. Backends use it to choose
/// <c>const</c> over <c>let</c> (effectively-immutable <c>var</c> locals,
/// pattern designators).
/// </summary>
public static class IrLocalReassignmentCollector
{
    /// <summary>The locals rebound anywhere in <paramref name="statements"/>.</summary>
    public static IReadOnlySet<string> CollectFromStatements(IEnumerable<IrStatement> statements)
    {
        var reassigned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var statement in statements)
            VisitStatement(statement, reassigned);

        return reassigned;
    }

    /// <summary>The locals rebound anywhere in <paramref name="expressions"/>
    /// (<c>null</c> entries are skipped).</summary>
    public static IReadOnlySet<string> CollectFromExpressions(
        IEnumerable<IrExpression?> expressions
    )
    {
        var reassigned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expression in expressions.OfType<IrExpression>())
            VisitExpression(expression, reassigned);

        return reassigned;
    }

    private static void VisitStatement(IrStatement statement, HashSet<string> reassigned)
    {
        foreach (var expression in IrNodeChildren.ExpressionsOf(statement))
            VisitExpression(expression, reassigned);

        foreach (var nested in IrNodeChildren.StatementsOf(statement))
            VisitStatement(nested, reassigned);
    }

    private static void VisitExpression(IrExpression expression, HashSet<string> reassigned)
    {
        if (RebindsLocal(expression) is { } name)
            reassigned.Add(name);

        foreach (var child in IrNodeChildren.ExpressionsOf(expression))
            VisitExpression(child, reassigned);

        foreach (var statement in IrNodeChildren.StatementsOf(expression))
            VisitStatement(statement, reassigned);
    }

    /// <summary>The local <paramref name="expression"/> rebinds, if any.</summary>
    private static string? RebindsLocal(IrExpression expression) =>
        expression switch
        {
            IrBinaryExpression { Left: IrIdentifier target } binary
                when binary.Operator.IsAssignment() => target.Name,
            IrUnaryExpression
            {
                Operator: IrUnaryOp.Increment or IrUnaryOp.Decrement,
                Operand: IrIdentifier target,
            } => target.Name,
            _ => null,
        };
}
