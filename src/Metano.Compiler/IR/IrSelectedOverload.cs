namespace Metano.Compiler.IR;

/// <summary>
/// The overload a call was lowered to (spec 013 FR-017), on a construction or
/// a base-constructor call whose arguments are then Roslyn's bound form: one
/// value per parameter, in parameter order, omitted defaults explicit.
/// </summary>
/// <param name="Target">The overload's identity.</param>
/// <param name="EvaluationOrder">The order the source evaluates the arguments
/// in, as indices into them, when it differs from parameter order and one of
/// them has a side effect (named arguments out of order): a target must
/// evaluate them in this order before it passes them. <c>null</c> when
/// parameter order is evaluation order.</param>
public sealed record IrSelectedOverload(
    IrOverloadRef Target,
    IReadOnlyList<int>? EvaluationOrder = null
)
{
    /// <summary>
    /// The synthesized value-equality member the overload is (spec 013 FR-043):
    /// its fast path keeps a stable name, so a target that cannot read the
    /// declaring type's claims (a referenced package) still names it.
    /// <c>null</c> for a declared overload.
    /// </summary>
    public IrSynthesizedValueMember? Synthesized { get; init; }
}
