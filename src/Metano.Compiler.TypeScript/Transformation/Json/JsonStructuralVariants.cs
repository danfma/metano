using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The write-side order of a hierarchy's <c>[PlainObject]</c> variants, or
/// why they cannot be told apart (spec 009 FR-019 rules 3–4).
/// <see cref="TestOrder"/> lists indices into the registrations, most
/// specific first; it is <c>null</c> when it equals attribute order (or there
/// is no structural variant), so the descriptor leaves it out.
/// </summary>
internal sealed record JsonStructuralDispatch(
    IReadOnlyList<int>? TestOrder,
    IReadOnlyList<JsonDiscriminatorConflict> Conflicts
);

/// <summary>
/// Orders the <c>[PlainObject]</c> variants of a polymorphic base by the
/// members their synthesized guards require (FR-019 rule 3): variant
/// <c>A</c> <em>covers</em> <c>B</c> when every required member of <c>A</c>
/// is a member of <c>B</c> with a compatible test kind, so <c>A</c>'s guard
/// may accept a <c>B</c>. When only <c>A</c> covers <c>B</c>, <c>B</c> is
/// more specific and is tested first; otherwise attribute order applies. Two
/// variants that cover each other, or a cycle in the "more specific"
/// relation, cannot be told apart on write: <c>MS0032</c> (rule 4). The
/// required members and kinds come from <see cref="TypeGuardBuilder"/>, the
/// builder that emits the guards.
/// </summary>
internal sealed class JsonStructuralVariants(TypeGuardBuilder guards)
{
    private readonly TypeGuardBuilder _guards = guards;

    public JsonStructuralDispatch DispatchOf(JsonPolymorphism polymorphism)
    {
        var variants = polymorphism
            .Registrations.Select(
                (registration, index) => (Registration: registration, Index: index)
            )
            .Where(entry => SymbolHelper.HasPlainObject(entry.Registration.Type))
            .Select(entry => new StructuralVariant(
                entry.Index,
                entry.Registration,
                _guards.GuardedMembersOf((INamedTypeSymbol)entry.Registration.Type)
            ))
            .ToList();

        var owner = OwnerOf(polymorphism);
        var conflicts = IndistinguishablePairsOf(variants, owner).ToList();

        if (conflicts.Count > 0)
            return new JsonStructuralDispatch(null, conflicts);

        var order = SpecificityOrderOf(variants);

        if (order.Unordered.Count > 0)
            return new JsonStructuralDispatch(null, [CycleConflictOf(order.Unordered, owner)]);

        var testOrder = order.Ordered.Select(variant => variant.Index).ToList();
        var isAttributeOrder = testOrder.SequenceEqual(variants.Select(variant => variant.Index));

        return new JsonStructuralDispatch(isAttributeOrder ? null : testOrder, []);
    }

    /// <summary>Every pair of variants that cover each other, in attribute order.</summary>
    private static IEnumerable<JsonDiscriminatorConflict> IndistinguishablePairsOf(
        IReadOnlyList<StructuralVariant> variants,
        string owner
    ) =>
        from first in variants
        from second in variants
        where first.Index < second.Index && Covers(first, second) && Covers(second, first)
        select new JsonDiscriminatorConflict(
            JsonPolymorphismMessages.IndistinguishableVariants(
                Display(first.Registration.Type),
                Display(second.Registration.Type),
                owner,
                SharedMembersOf(first, second)
            ),
            second.Registration.Location
        );

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
        && Covers(general, specific)
        && !Covers(specific, general);

    /// <summary>
    /// Every required member of <paramref name="covering"/> is a member of
    /// <paramref name="covered"/> with a compatible kind, so the guard of
    /// <paramref name="covering"/> may accept a <paramref name="covered"/>
    /// value.
    /// </summary>
    private static bool Covers(StructuralVariant covering, StructuralVariant covered) =>
        covering
            .Members.Where(member => member.IsRequired)
            .All(required =>
                covered.Members.Any(member =>
                    member.Name == required.Name && AreCompatible(member.Kind, required.Kind)
                )
            );

    private static bool AreCompatible(GuardTestKind first, GuardTestKind second) =>
        first == second
        || first is GuardTestKind.Present or GuardTestKind.Unchecked
        || second is GuardTestKind.Present or GuardTestKind.Unchecked;

    /// <summary>
    /// The required members of both variants, as the message lists them
    /// (<c>name: string</c>), first variant's first.
    /// </summary>
    private static IReadOnlyList<string> SharedMembersOf(
        StructuralVariant first,
        StructuralVariant second
    ) =>
        first
            .Members.Concat(second.Members)
            .Where(member => member.IsRequired)
            .Select(member => $"{member.Name}: {KindNameOf(member.Kind)}")
            .Distinct()
            .ToList();

    private static JsonDiscriminatorConflict CycleConflictOf(
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

    private static string KindNameOf(GuardTestKind kind) =>
        kind switch
        {
            GuardTestKind.BigInt => "bigint",
            _ => kind.ToString().ToLowerInvariant(),
        };

    private static string OwnerOf(JsonPolymorphism polymorphism) =>
        Display(polymorphism.BaseType.OriginalDefinition);

    private static string Display(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    private sealed record StructuralVariant(
        int Index,
        JsonDerivedTypeRegistration Registration,
        IReadOnlyList<GuardedMember> Members
    );
}
