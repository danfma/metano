using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The locals a pattern declares. C# scopes them to the enclosing statement
/// (or switch section), so a backend without expression-scoped variables
/// declares each one ahead of the test that assigns it.
/// </summary>
public static class IrPatternDesignators
{
    /// <summary>
    /// Every designator in <paramref name="pattern"/>, in source order,
    /// including those under <c>not</c> (definitely assigned when the pattern
    /// fails) and in a list pattern's slice.
    /// </summary>
    public static IEnumerable<IrPatternDesignator> DeclaredBy(IrPattern pattern) =>
        pattern switch
        {
            IrTypePattern { Designator: { } designator } => [designator],
            IrVarPattern varPattern => [varPattern.Designator],
            IrPropertyPattern property => Own(property.Designator)
                .Concat(property.Subpatterns.SelectMany(sub => DeclaredBy(sub.Pattern))),
            IrPositionalPattern positional => Own(positional.Designator)
                .Concat(positional.Elements.SelectMany(DeclaredBy)),
            IrListPattern list => list
                .Elements.SelectMany(DeclaredBy)
                .Concat(list.SlicePattern is null ? [] : DeclaredBy(list.SlicePattern)),
            IrLogicalPattern logical => DeclaredBy(logical.Left)
                .Concat(logical.Right is null ? [] : DeclaredBy(logical.Right)),
            _ => [],
        };

    /// <summary>
    /// The designators of the <c>is</c> patterns <paramref name="expression"/>
    /// evaluates, outside nested lambdas (whose bodies declare their own).
    /// </summary>
    public static IEnumerable<IrPatternDesignator> DeclaredIn(IrExpression expression)
    {
        var own = expression is IrIsPatternExpression isPattern
            ? DeclaredBy(isPattern.Pattern)
            : [];

        return own.Concat(IrNodeChildren.ExpressionsOf(expression).SelectMany(DeclaredIn));
    }

    /// <summary>Whether the pattern declares any local.</summary>
    public static bool Declares(IrPattern pattern) => DeclaredBy(pattern).Any();

    private static IEnumerable<IrPatternDesignator> Own(IrPatternDesignator? designator) =>
        designator is null ? [] : [designator];
}
