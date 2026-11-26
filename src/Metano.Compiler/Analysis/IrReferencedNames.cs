using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Name-based reference lookup over IR. Conservative: a same-named local of a
/// nested lambda counts as a reference too, which only ever makes a caller
/// bind a name it did not strictly need.
/// </summary>
public static class IrReferencedNames
{
    private delegate bool NodeMatch(IrExpression node, HashSet<string> names);

    /// <summary>
    /// Whether <paramref name="expression"/> — nested lambda bodies included —
    /// reads an identifier named in <paramref name="names"/>.
    /// </summary>
    public static bool AnyIn(IrExpression expression, IEnumerable<string> names) =>
        Any(expression, names, IsIdentifierIn);

    /// <summary>
    /// Whether any of <paramref name="statements"/> — nested statements and
    /// lambda bodies included — reads an identifier named in
    /// <paramref name="names"/>.
    /// </summary>
    public static bool AnyIn(IEnumerable<IrStatement> statements, IEnumerable<string> names) =>
        Any(statements, names, IsIdentifierIn);

    /// <summary>
    /// Whether <paramref name="expression"/> — nested lambda bodies included —
    /// reads a name in <paramref name="names"/>, either as an identifier or as
    /// a member of <c>this</c> (<c>this.name</c>).
    /// </summary>
    public static bool AnyReadOrThisMemberIn(IrExpression expression, IEnumerable<string> names) =>
        Any(expression, names, IsIdentifierOrThisMemberIn);

    /// <summary>
    /// Whether <paramref name="expression"/> — nested lambda bodies included —
    /// names one of <paramref name="names"/> either as an identifier or as an
    /// emitted name a target may print bare: the <c>[Name]</c> override of a
    /// member it accesses or calls, or of a type it constructs, casts to,
    /// tests, or reads a static member of.
    /// </summary>
    public static bool AnyReadOrEmittedNameIn(IrExpression expression, IEnumerable<string> names) =>
        Any(expression, names, IsIdentifierOrEmittedNameIn);

    /// <summary>
    /// <see cref="AnyReadOrEmittedNameIn(IrExpression, IEnumerable{string})"/>
    /// over <paramref name="statements"/>, nested statements included.
    /// </summary>
    public static bool AnyReadOrEmittedNameIn(
        IEnumerable<IrStatement> statements,
        IEnumerable<string> names
    ) => Any(statements, names, IsIdentifierOrEmittedNameIn);

    private static bool Any(IrExpression expression, IEnumerable<string> names, NodeMatch match)
    {
        var wanted = names.ToHashSet(StringComparer.Ordinal);

        return wanted.Count > 0 && Reads(expression, node => match(node, wanted));
    }

    private static bool Any(
        IEnumerable<IrStatement> statements,
        IEnumerable<string> names,
        NodeMatch match
    )
    {
        var wanted = names.ToHashSet(StringComparer.Ordinal);

        return wanted.Count > 0
            && statements.Any(statement => Reads(statement, node => match(node, wanted)));
    }

    private static bool IsIdentifierOrEmittedNameIn(IrExpression node, HashSet<string> names) =>
        IsIdentifierIn(node, names) || IsEmittedNameIn(node, names);

    private static bool IsIdentifierOrThisMemberIn(IrExpression node, HashSet<string> names) =>
        IsIdentifierIn(node, names) || IsThisMemberIn(node, names);

    private static bool IsEmittedNameIn(IrExpression node, HashSet<string> names) =>
        node switch
        {
            IrMemberAccess { Origin.EmittedName: { } emitted } => names.Contains(emitted),
            IrCallExpression { Origin.EmittedName: { } emitted } => names.Contains(emitted),
            IrTypeReference reference => IsTypeNamed(reference.Name, names),
            IrNewExpression creation => IsTypeNamed(creation.Type, names),
            IrCastExpression cast => IsTypeNamed(cast.TargetType, names),
            IrUnsafeCastExpression unsafeCast => IsTypeNamed(unsafeCast.Target, names),
            IrInlineCodeExpression { ResultType: { } resultType } => IsTypeNamed(resultType, names),
            IrTypeCheck check => IsTypeNamed(check.Type, names),
            IrTypeOfExpression typeOf => IsTypeNamed(typeOf.Type, names),
            IrGetTypeExpression { ConstantType: { } constant } => IsTypeNamed(constant, names),
            _ => false,
        };

    private static bool IsTypeNamed(IrTypeRef type, HashSet<string> names) =>
        type is IrNamedTypeRef named && IsTypeNamed(named.Name, names);

    /// <summary>
    /// Whether the emitted type name <paramref name="typeName"/>, or the
    /// outermost type of its dotted path (<c>Outer.Inner</c>), is wanted.
    /// </summary>
    private static bool IsTypeNamed(string typeName, HashSet<string> names) =>
        names.Contains(typeName) || names.Contains(typeName.Split('.')[0]);

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
