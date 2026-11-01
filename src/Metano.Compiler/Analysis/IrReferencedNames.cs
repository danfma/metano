using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Name-based reference lookup over IR. Conservative: a same-named local of a
/// nested lambda counts as a reference too, which only ever makes a caller
/// bind a name it did not strictly need.
/// </summary>
public static class IrReferencedNames
{
    /// <summary>
    /// Whether <paramref name="expression"/> — nested lambda bodies included —
    /// reads an identifier named in <paramref name="names"/>.
    /// </summary>
    public static bool AnyIn(IrExpression expression, IEnumerable<string> names)
    {
        var wanted = names.ToHashSet(StringComparer.Ordinal);

        return wanted.Count > 0 && Reads(expression, node => IsIdentifierIn(node, wanted));
    }

    /// <summary>
    /// Whether <paramref name="expression"/> — nested lambda bodies included —
    /// reads a name in <paramref name="names"/>, either as an identifier or as
    /// a member of <c>this</c> (<c>this.name</c>).
    /// </summary>
    public static bool AnyReadOrThisMemberIn(IrExpression expression, IEnumerable<string> names)
    {
        var wanted = names.ToHashSet(StringComparer.Ordinal);

        return wanted.Count > 0
            && Reads(
                expression,
                node => IsIdentifierIn(node, wanted) || IsThisMemberIn(node, wanted)
            );
    }

    private static bool IsIdentifierIn(IrExpression node, HashSet<string> names) =>
        node is IrIdentifier identifier && names.Contains(identifier.Name);

    private static bool IsThisMemberIn(IrExpression node, HashSet<string> names) =>
        node is IrMemberAccess { Target: IrThisExpression } access
        && names.Contains(access.MemberName);

    private static bool Reads(IrExpression expression, Func<IrExpression, bool> matches) =>
        matches(expression)
        || IrNodeChildren.ExpressionsOf(expression).Any(child => Reads(child, matches))
        || IrNodeChildren.StatementsOf(expression).Any(statement => Reads(statement, matches));

    private static bool Reads(IrStatement statement, Func<IrExpression, bool> matches) =>
        IrNodeChildren.ExpressionsOf(statement).Any(child => Reads(child, matches))
        || IrNodeChildren.StatementsOf(statement).Any(nested => Reads(nested, matches));
}
