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
    /// For each parameter position where the dispatcher's overloads take
    /// delegates of different arities, the fewest parameters among them
    /// (<see cref="WithDelegateAritiesOf"/>); <c>null</c> when none differ.
    /// </summary>
    public IReadOnlyDictionary<int, int>? LowestDelegateArities { get; init; }

    /// <summary>
    /// The <c>Function.length</c> a delegate argument must at least have: set
    /// for a delegate of more parameters than the fewest at its position, so
    /// the delegate of fewest parameters still takes any function (a
    /// callback that ignores its arguments, a defaulted or rest parameter).
    /// </summary>
    public int? DelegateArityFloor { get; init; }

    /// <summary>
    /// This scope over a dispatcher of overloads taking
    /// <paramref name="parameterLists"/>: the positions whose delegate
    /// arities differ get a minimum-length test (#778).
    /// </summary>
    public TypeCheckScope WithDelegateAritiesOf(
        IEnumerable<IReadOnlyList<IrParameter>> parameterLists
    )
    {
        var lowest = parameterLists
            .SelectMany(parameters =>
                parameters.Select(
                    (parameter, index) => (index, Arity: DelegateArityOf(parameter.Type))
                )
            )
            .Where(entry => entry.Arity is not null)
            .GroupBy(entry => entry.index, entry => entry.Arity!.Value)
            .Where(group => group.Distinct().Count() > 1)
            .ToDictionary(group => group.Key, group => group.Min());

        return this with
        {
            LowestDelegateArities = lowest.Count == 0 ? null : lowest,
        };
    }

    /// <summary>The scope that checks the argument of the parameter at <paramref name="index"/>.</summary>
    public TypeCheckScope ForParameter(int index, IrTypeRef type) =>
        LowestDelegateArities?.TryGetValue(index, out var lowest) == true
        && DelegateArityOf(type) is { } arity
        && arity > lowest
            ? this with
            {
                DelegateArityFloor = arity,
            }
            : this;

    private static int? DelegateArityOf(IrTypeRef type) =>
        type switch
        {
            IrNullableTypeRef nullable => DelegateArityOf(nullable.Inner),
            IrFunctionTypeRef function => function.Parameters.Count,
            _ => null,
        };

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
            LowestDelegateArities = LowestDelegateArities,
        };
}
