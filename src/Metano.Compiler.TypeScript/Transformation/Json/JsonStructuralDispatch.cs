namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The write-side order of a hierarchy's <c>[PlainObject]</c> variants, or
/// why their guards cannot tell values apart (spec 009 FR-019 rules 3–5).
/// <see cref="TestOrder"/> lists indices into the registrations, most
/// specific first. It is <c>null</c> in two cases the descriptor treats
/// alike (it leaves <c>testOrder</c> out): the order equals attribute order,
/// or there is no structural variant.
/// </summary>
internal sealed record JsonStructuralDispatch(
    IReadOnlyList<int>? TestOrder,
    IReadOnlyList<JsonPolymorphismConflict> Conflicts
);
