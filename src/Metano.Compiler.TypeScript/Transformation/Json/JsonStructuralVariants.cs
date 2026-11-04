using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Orders the <c>[PlainObject]</c> variants of a polymorphic base by the
/// members their synthesized guards require (FR-019 rule 3): variant
/// <c>A</c> <em>covers</em> <c>B</c> when every required member of <c>A</c>
/// is a member of <c>B</c> with a compatible test, so <c>A</c>'s guard may
/// accept a <c>B</c>. When only <c>A</c> covers <c>B</c>, <c>B</c> is more
/// specific and is tested first; otherwise attribute order applies.
/// <c>MS0032</c> (rule 4) reports guards that cannot tell values apart: two
/// variants that cover each other, a cycle in the "more specific" relation,
/// and a variant that covers the base when the base's own guard is the
/// rule 5 <c>baseTest</c> (the variant guards run first, so it would capture
/// every base value). The required members and tests come from
/// <see cref="TypeGuardBuilder"/>, the builder that emits the guards.
/// </summary>
internal sealed class JsonStructuralVariants(TypeGuardBuilder guards)
{
    private readonly TypeGuardBuilder _guards = guards;

    /// <summary>
    /// Whether <paramref name="type"/> gets a guard the descriptor can
    /// reference (no guard for <c>[Import]</c>, <c>[ExportedAsModule]</c>,
    /// <c>[NoContainer]</c>, exception types or types with extension
    /// members).
    /// </summary>
    public bool HasGuard(INamedTypeSymbol type) => _guards.GeneratePredicate(type) is not null;

    public JsonStructuralDispatch DispatchOf(JsonPolymorphism polymorphism)
    {
        var variants = StructuralVariantsOf(polymorphism);
        var owner = Display(polymorphism.BaseType.OriginalDefinition);

        var conflicts = IndistinguishablePairsOf(variants, owner)
            .Concat(BaseCapturesOf(polymorphism, variants, owner))
            .ToList();

        if (conflicts.Count > 0)
            return new JsonStructuralDispatch(null, conflicts);

        var order = SpecificityOrderOf(variants);

        if (order.Unordered.Count > 0)
            return new JsonStructuralDispatch(null, [CycleConflictOf(order.Unordered, owner)]);

        var testOrder = order.Ordered.Select(variant => variant.Index).ToList();
        var isAttributeOrder = testOrder.SequenceEqual(variants.Select(variant => variant.Index));

        return new JsonStructuralDispatch(isAttributeOrder ? null : testOrder, []);
    }

    /// <summary>The structural registrations, with their index and guarded members.</summary>
    private List<StructuralVariant> StructuralVariantsOf(JsonPolymorphism polymorphism) =>
        polymorphism
            .Registrations.Select(
                (registration, index) => (Registration: registration, Index: index)
            )
            .Where(entry => entry.Registration.IsStructural)
            .Select(entry => new StructuralVariant(
                entry.Index,
                entry.Registration,
                _guards.GuardedMembersOf((INamedTypeSymbol)entry.Registration.Type)
            ))
            .ToList();

    /// <summary>Every pair of variants that cover each other, in attribute order.</summary>
    private static IEnumerable<JsonPolymorphismConflict> IndistinguishablePairsOf(
        IReadOnlyList<StructuralVariant> variants,
        string owner
    ) =>
        from first in variants
        from second in variants
        where
            first.Index < second.Index
            && Covers(first.Members, second.Members)
            && Covers(second.Members, first.Members)
        select new JsonPolymorphismConflict(
            JsonPolymorphismMessages.IndistinguishableVariants(
                Display(first.Registration.Type),
                Display(second.Registration.Type),
                owner,
                RequiredMembersOfEither(first.Members, second.Members)
            ),
            second.Registration.Location
        );

    /// <summary>
    /// Every variant whose guard accepts a value of the base, when the base
    /// is written through its <c>baseTest</c> after every variant guard
    /// (FR-019 rule 5): the variant would capture every base value.
    /// </summary>
    private IEnumerable<JsonPolymorphismConflict> BaseCapturesOf(
        JsonPolymorphism polymorphism,
        IReadOnlyList<StructuralVariant> variants,
        string owner
    )
    {
        if (!polymorphism.NeedsBaseTest)
            return [];

        var baseMembers = _guards.GuardedMembersOf(polymorphism.BaseType);

        return variants
            .Where(variant => Covers(variant.Members, baseMembers))
            .Select(variant => new JsonPolymorphismConflict(
                JsonPolymorphismMessages.VariantCapturesBase(
                    Display(variant.Registration.Type),
                    owner,
                    RequiredMembersOfEither(variant.Members, [])
                ),
                variant.Registration.Location
            ));
    }

    /// <summary>
    /// A stable topological order of the "more specific" relation: at each
    /// step, the first variant in attribute order that no remaining variant
    /// must precede. Variants left over form a cycle.
    /// </summary>
    private static (
        List<StructuralVariant> Ordered,
        List<StructuralVariant> Unordered
    ) SpecificityOrderOf(IReadOnlyList<StructuralVariant> variants)
    {
        var remaining = variants.ToList();
        var ordered = new List<StructuralVariant>();

        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(candidate =>
                !remaining.Any(other => IsMoreSpecific(other, candidate))
            );

            if (next is null)
                break;

            ordered.Add(next);
            remaining.Remove(next);
        }

        return (ordered, remaining);
    }

    /// <summary>
    /// <paramref name="specific"/> must be tested before
    /// <paramref name="general"/>: the general guard covers it, not the
    /// reverse.
    /// </summary>
    private static bool IsMoreSpecific(StructuralVariant specific, StructuralVariant general) =>
        !ReferenceEquals(specific, general)
        && Covers(general.Members, specific.Members)
        && !Covers(specific.Members, general.Members);

    /// <summary>
    /// Every required member of <paramref name="covering"/> is a member of
    /// <paramref name="covered"/> with a compatible test, so the guard of
    /// <paramref name="covering"/> may accept a value of
    /// <paramref name="covered"/>.
    /// </summary>
    private static bool Covers(
        IReadOnlyList<GuardedMember> covering,
        IReadOnlyList<GuardedMember> covered
    ) =>
        covering
            .Where(member => member.IsRequired)
            .All(required =>
                covered.Any(member =>
                    member.Name == required.Name && member.IsCompatibleWith(required)
                )
            );

    /// <summary>
    /// The members either guard requires, as the message lists them
    /// (<c>name: string</c>), the first list's first.
    /// </summary>
    private static IReadOnlyList<string> RequiredMembersOfEither(
        IReadOnlyList<GuardedMember> first,
        IReadOnlyList<GuardedMember> second
    ) =>
        first
            .Concat(second)
            .Where(member => member.IsRequired)
            .Select(member => $"{member.Name}: {KindNameOf(member)}")
            .Distinct()
            .ToList();

    private static JsonPolymorphismConflict CycleConflictOf(
        IReadOnlyList<StructuralVariant> cycle,
        string owner
    ) =>
        new(
            JsonPolymorphismMessages.UnorderedVariants(
                cycle.Select(variant => Display(variant.Registration.Type)).ToList(),
                owner
            ),
            cycle[^1].Registration.Location
        );

    private static string KindNameOf(GuardedMember member) =>
        member.Literal is { } literal
            ? $"\"{literal}\""
            : member.Kind.ToString().ToLowerInvariant();

    private static string Display(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    private sealed record StructuralVariant(
        int Index,
        JsonDerivedTypeRegistration Registration,
        IReadOnlyList<GuardedMember> Members
    );
}
