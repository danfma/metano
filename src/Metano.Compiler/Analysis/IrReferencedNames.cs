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

        return wanted.Count > 0 && Reads(expression, wanted);
    }

    private static bool Reads(IrExpression expression, HashSet<string> names) =>
        expression is IrIdentifier identifier && names.Contains(identifier.Name)
        || IrNodeChildren.ExpressionsOf(expression).Any(child => Reads(child, names))
        || IrNodeChildren.StatementsOf(expression).Any(statement => Reads(statement, names));

    private static bool Reads(IrStatement statement, HashSet<string> names) =>
        IrNodeChildren.ExpressionsOf(statement).Any(child => Reads(child, names))
        || IrNodeChildren.StatementsOf(statement).Any(nested => Reads(nested, names));
}
