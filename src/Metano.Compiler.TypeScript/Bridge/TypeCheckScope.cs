using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What a dispatcher's runtime type checks resolve against (see
/// <see cref="IrTypeCheckBuilder.Generate"/>).
/// </summary>
/// <param name="TypeParameters">The type parameters in scope, innermost first;
/// a type parameter reference resolves against them, and an unresolved one
/// matches any value.</param>
/// <param name="TypeOverrides">The <c>[ExportFromBcl]</c> type overrides the
/// checked types were mapped with: a type that lowers to an exported class
/// (<c>decimal</c> → <c>Decimal</c>) is checked with <c>instanceof</c> that
/// class; <c>null</c> keeps the default mapping.</param>
internal sealed record TypeCheckScope(
    IReadOnlyList<IrTypeParameter> TypeParameters,
    IrToTsTypeOverrides? TypeOverrides
)
{
    /// <summary>No type parameter and no override.</summary>
    public static TypeCheckScope Empty { get; } = new([], null);

    /// <summary>
    /// The reached set a descriptor read the checks render is asserted against
    /// (the file group's <see cref="IrToTsBridgeContext.RuntimeTypes"/>);
    /// <see cref="Analysis.RuntimeTypeReachabilityResult.Unchecked"/> for a scope
    /// that renders no output (an ambiguity analysis).
    /// </summary>
    public Analysis.RuntimeTypeReachabilityResult RuntimeTypes { get; init; } =
        Analysis.RuntimeTypeReachabilityResult.Empty;

    /// <summary>
    /// The parameter lists of every overload the dispatcher tries
    /// (<see cref="WithDelegateAritiesOf"/>), which a delegate argument's
    /// arity test is told apart against; <c>null</c> outside a dispatcher.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IrParameter>>? DispatchedParameterLists { get; init; }

    /// <summary>
    /// The <c>Function.length</c> range a delegate argument must fall in
    /// (<see cref="DelegateArityRange"/>); <c>null</c> to test only that it
    /// is a function.
    /// </summary>
    public DelegateArityRange? DelegateArity { get; init; }

    /// <summary>
    /// This scope over a dispatcher of overloads taking
    /// <paramref name="parameterLists"/>, whose delegate arguments of
    /// different arities are told apart by non-overlapping length ranges
    /// (#778, <see cref="DelegateArityRange"/>).
    /// </summary>
    public TypeCheckScope WithDelegateAritiesOf(
        IEnumerable<IReadOnlyList<IrParameter>> parameterLists
    ) => this with { DispatchedParameterLists = [.. parameterLists] };

    /// <summary>
    /// The scope of a method: its own type parameters, then its class's, so a
    /// method type parameter shadows a class one of the same name.
    /// </summary>
    public static TypeCheckScope ForMethod(
        IrMethodDeclaration method,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        IrToTsTypeOverrides? typeOverrides
    ) => new([.. method.TypeParameters ?? [], .. classTypeParameters], typeOverrides);

    /// <summary>
    /// The scope of <paramref name="method"/> inside this class scope, keeping
    /// its overrides and reached set.
    /// </summary>
    public TypeCheckScope Within(IrMethodDeclaration method) =>
        ForMethod(method, TypeParameters, TypeOverrides) with
        {
            RuntimeTypes = RuntimeTypes,
            DispatchedParameterLists = DispatchedParameterLists,
        };
}

/// <summary>
/// The <c>Function.length</c> range a delegate argument of a dispatcher must
/// fall in, at a parameter position where the overloads whose argument
/// counts overlap take delegates of different arities (#778). The ranges of
/// those arities do not overlap, so the dispatch order does not matter:
/// arities {0, 2, 3} test <c>&lt; 2</c>, <c>&gt;= 2 &amp;&amp; &lt; 3</c>
/// and <c>&gt;= 3</c>. The smallest has no lower bound, so a callback that
/// ignores its arguments (<c>() =&gt; 42</c>) still lands; a defaulted
/// parameter shortens <c>length</c>, so such a callback reaches a smaller
/// arity.
/// </summary>
/// <param name="AtLeast">The lower bound, <c>null</c> for the smallest arity.</param>
/// <param name="Below">The exclusive upper bound, <c>null</c> for the largest arity.</param>
internal sealed record DelegateArityRange(int? AtLeast, int? Below)
{
    /// <summary>
    /// The range of a delegate of <paramref name="arity"/> parameters among
    /// <paramref name="arities"/> (the arities at its position); <c>null</c>
    /// when they are all the same.
    /// </summary>
    public static DelegateArityRange? Of(int arity, IEnumerable<int> arities)
    {
        var distinct = arities.Append(arity).Distinct().Order().ToList();

        if (distinct.Count < 2)
            return null;

        var index = distinct.IndexOf(arity);

        return new DelegateArityRange(
            index == 0 ? null : arity,
            index == distinct.Count - 1 ? null : distinct[index + 1]
        );
    }

    /// <summary>The parameter count of the delegate <paramref name="type"/> is, through nullability; <c>null</c> for any other type.</summary>
    public static int? ArityOf(IrTypeRef type) =>
        type switch
        {
            IrNullableTypeRef nullable => ArityOf(nullable.Inner),
            IrFunctionTypeRef function => function.Parameters.Count,
            _ => null,
        };
}
