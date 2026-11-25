using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Why a closed family keeps the plain abstract-class shape (spec 010 FR-C5).
/// </summary>
internal enum ClosedFamilySuppression
{
    None,
    KindMember,
    DuplicateTag,
    NoEmitOrPlainObject,
    InexpressibleGenericCase,
    IncompleteCases,
    RuntimeClassNameTaken,
    InsideOpenCase,

    /// <summary>The family declares <c>[Discriminator]</c>: suppressed silently.</summary>
    Discriminator,
}

/// <summary>
/// The TypeScript shape of one closed family (spec 010 FR-C5, #738, decision W8-2):
/// its root, whether it lowers to a union with a <c>kind</c> discriminant (and why not),
/// the tags under each closed type of the family, and the tag of each tag owner.
/// </summary>
internal sealed record ClosedFamilyPlan(
    INamedTypeSymbol Root,
    ClosedFamilySuppression Suppression,
    IReadOnlyDictionary<INamedTypeSymbol, IReadOnlyList<string>> TagsByClosedType,
    IReadOnlyDictionary<INamedTypeSymbol, string> TagByOwner
)
{
    public bool IsLowered => Suppression is ClosedFamilySuppression.None;
}

/// <summary>
/// Decides, once per closed family and from symbols alone, whether the family lowers
/// to the union shape: the root's emission, each case's emission and every pattern
/// that binds a family type read the same answer. A family lowers when nothing in
/// <see cref="ClosedFamilySuppression"/> applies.
/// </summary>
internal sealed class ClosedFamilyLowering(Func<INamedTypeSymbol, string> tagNameOf)
{
    /// <summary>The discriminant's TypeScript member name.</summary>
    public const string KindMemberName = "kind";

    /// <summary>The suffix of a lowered closed type's runtime class.</summary>
    public const string RuntimeClassSuffix = "$Base";

    private readonly Func<INamedTypeSymbol, string> _tagNameOf = tagNameOf;

    /// <summary>The plans per family root; file groups transform in parallel.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        INamedTypeSymbol,
        ClosedFamilyPlan
    > _plans = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The plan of the family <paramref name="closedType"/> belongs to; <c>null</c> when
    /// it is not a closed class.
    /// </summary>
    public ClosedFamilyPlan? PlanOf(INamedTypeSymbol closedType)
    {
        var definition = closedType.OriginalDefinition;

        if (!definition.IsClosed)
            return null;

        return _plans.GetOrAdd(RootOf(definition), Plan);
    }

    /// <summary>Whether <paramref name="closedType"/> lowers to the union shape.</summary>
    public bool IsLowered(INamedTypeSymbol closedType) => PlanOf(closedType)?.IsLowered == true;

    /// <summary>
    /// The tag <paramref name="type"/> owns as a case of a lowered family; <c>null</c>
    /// when it owns none.
    /// </summary>
    public string? TagOf(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;

        if (
            definition.IsClosed
            || definition.BaseType?.OriginalDefinition is not { IsClosed: true } closedBase
        )
            return null;

        return
            PlanOf(closedBase) is { IsLowered: true } plan
            && plan.TagByOwner.TryGetValue(definition, out var tag)
            ? tag
            : null;
    }

    /// <summary>
    /// The MS0073 warning for a suppressed family, reported at its root; <c>null</c>
    /// for a lowered family or a silent suppression.
    /// </summary>
    public static MetanoDiagnostic? SuppressionDiagnostic(ClosedFamilyPlan plan)
    {
        var reason = plan.Suppression switch
        {
            ClosedFamilySuppression.KindMember =>
                $"a type of the family already emits a '{KindMemberName}' member",
            ClosedFamilySuppression.DuplicateTag => "two cases share the same TypeScript name",
            ClosedFamilySuppression.NoEmitOrPlainObject =>
                "a type of the family is [NoEmit] or [PlainObject]",
            ClosedFamilySuppression.InexpressibleGenericCase =>
                "a case's generic shape cannot be written as a member of the union",
            ClosedFamilySuppression.IncompleteCases => "the set of cases is not complete",
            ClosedFamilySuppression.RuntimeClassNameTaken =>
                $"a nested type takes the runtime class name '{plan.Root.Name}{RuntimeClassSuffix}'",
            ClosedFamilySuppression.InsideOpenCase =>
                "the family derives from an open case of another closed family",
            _ => null,
        };

        return reason is null
            ? null
            : new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.ClosedFamilyTagSuppressed,
                $"Closed family '{plan.Root.Name}' keeps the abstract-class TypeScript shape, "
                    + $"without the union type and its '{KindMemberName}' discriminant: {reason}.",
                plan.Root.Locations.FirstOrDefault()
            );
    }

    private static INamedTypeSymbol RootOf(INamedTypeSymbol closedType)
    {
        var root = closedType;

        while (root.BaseType?.OriginalDefinition is { IsClosed: true } closedBase)
            root = closedBase;

        return root;
    }

    private ClosedFamilyPlan Plan(INamedTypeSymbol root)
    {
        var tagsByClosedType = new Dictionary<INamedTypeSymbol, IReadOnlyList<string>>(
            SymbolEqualityComparer.Default
        );

        var tagByOwner = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        var walk = new FamilyWalk(tagsByClosedType, tagByOwner);

        CollectTags(root, walk);

        var suppression = SuppressionOf(root, walk);

        return new ClosedFamilyPlan(root, suppression, tagsByClosedType, tagByOwner);
    }

    private sealed record FamilyWalk(
        Dictionary<INamedTypeSymbol, IReadOnlyList<string>> TagsByClosedType,
        Dictionary<INamedTypeSymbol, string> TagByOwner
    )
    {
        public bool IsIncomplete { get; set; }

        public bool HasInexpressibleCase { get; set; }

        public List<INamedTypeSymbol> Members { get; } = [];
    }

    private IReadOnlyList<string> CollectTags(INamedTypeSymbol closedType, FamilyWalk walk)
    {
        var set = ClosedHierarchy.CasesOf(closedType);
        var tags = new List<string>();

        walk.Members.Add(closedType);
        walk.IsIncomplete |= !set.IsComplete;

        foreach (var closedCase in set.Cases.Where(c => IsEmitted(c.Case)))
        {
            walk.HasInexpressibleCase |= closedCase.Shape is ClosedCaseShape.Other;

            if (closedCase.Case.IsClosed)
            {
                tags.AddRange(CollectTags(closedCase.Case, walk));

                continue;
            }

            var tag = _tagNameOf(closedCase.Case);

            walk.Members.Add(closedCase.Case);
            walk.TagByOwner[closedCase.Case] = tag;
            tags.Add(tag);
        }

        walk.TagsByClosedType[closedType] = tags;

        return tags;
    }

    private static ClosedFamilySuppression SuppressionOf(INamedTypeSymbol root, FamilyWalk walk)
    {
        if (walk.Members.Any(type => type.GetDiscriminatorFieldName() is not null))
            return ClosedFamilySuppression.Discriminator;

        if (HasClosedAncestor(root))
            return ClosedFamilySuppression.InsideOpenCase;

        if (walk.IsIncomplete)
            return ClosedFamilySuppression.IncompleteCases;

        if (walk.HasInexpressibleCase)
            return ClosedFamilySuppression.InexpressibleGenericCase;

        if (walk.Members.Any(IsNoEmitOrPlainObject))
            return ClosedFamilySuppression.NoEmitOrPlainObject;

        if (walk.TagByOwner.Values.GroupBy(tag => tag).Any(group => group.Count() > 1))
            return ClosedFamilySuppression.DuplicateTag;

        var kindCandidates = walk
            .Members.Concat(EmittedAncestors(root))
            .Concat(DescendantsOfOpenCases(walk));

        if (kindCandidates.Any(DeclaresKindMember))
            return ClosedFamilySuppression.KindMember;

        if (walk.TagsByClosedType.Keys.Any(TakesRuntimeClassName))
            return ClosedFamilySuppression.RuntimeClassNameTaken;

        return ClosedFamilySuppression.None;
    }

    private static bool IsEmitted(INamedTypeSymbol type) =>
        !SymbolHelper.HasAttribute(type, "NoTranspile")
        && !SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript);

    private static bool HasClosedAncestor(INamedTypeSymbol root)
    {
        for (var ancestor = root.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (ancestor.OriginalDefinition.IsClosed)
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EmittedAncestors(INamedTypeSymbol root)
    {
        for (var ancestor = root.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (ancestor.SpecialType is SpecialType.System_Object)
                yield break;

            yield return ancestor.OriginalDefinition;
        }
    }

    private static bool IsNoEmitOrPlainObject(INamedTypeSymbol type) =>
        SymbolHelper.HasAttribute(type, "NoEmit") || type.HasPlainObject();

    /// <summary>
    /// <paramref name="type"/> declares an instance member TypeScript emits as
    /// <c>kind</c>, an explicit interface implementation (<c>string IKinded.Kind</c>)
    /// included: it lowers to a public member named after the interface's.
    /// </summary>
    private static bool DeclaresKindMember(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(member => !member.IsStatic && !member.IsImplicitlyDeclared)
            .SelectMany(EmittedNamesOf)
            .Contains(KindMemberName);

    private static IEnumerable<string> EmittedNamesOf(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => NamesOf(
                property,
                property.ExplicitInterfaceImplementations
            ),
            IEventSymbol @event => NamesOf(@event, @event.ExplicitInterfaceImplementations),
            IMethodSymbol
            {
                MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation,
            } method => NamesOf(method, method.ExplicitInterfaceImplementations),
            IFieldSymbol field => [EmittedNameOf(field)],
            _ => [],
        };

    private static IEnumerable<string> NamesOf<T>(T member, IEnumerable<T> implemented)
        where T : ISymbol
    {
        var interfaceMembers = implemented.ToList();

        return interfaceMembers.Count > 0
            ? interfaceMembers.Select(m => EmittedNameOf(m))
            : [EmittedNameOf(member)];
    }

    private static string EmittedNameOf(ISymbol member) =>
        member.GetNameOverride(TargetLanguage.TypeScript) ?? SymbolHelper.ToCamelCase(member.Name);

    /// <summary>
    /// The types of the open cases' own assembly that derive from them: they inherit
    /// the case's tag accessor, so a <c>kind</c> member there suppresses the family.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> DescendantsOfOpenCases(FamilyWalk walk)
    {
        var openCases = walk
            .TagByOwner.Keys.Where(owner => !owner.IsSealed)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (openCases.Count == 0)
            return [];

        return openCases
            .Select(owner => owner.ContainingAssembly)
            .Distinct<IAssemblySymbol>(SymbolEqualityComparer.Default)
            .SelectMany(assembly => TypesIn(assembly.GlobalNamespace))
            .Where(type => InheritsFromAny(type, openCases));
    }

    private static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceOrTypeSymbol container) =>
        container
            .GetMembers()
            .OfType<INamespaceOrTypeSymbol>()
            .SelectMany(member =>
                member is INamedTypeSymbol type ? [type, .. TypesIn(type)] : TypesIn(member)
            );

    private static bool InheritsFromAny(
        INamedTypeSymbol type,
        IReadOnlySet<INamedTypeSymbol> ancestors
    )
    {
        for (var ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (ancestors.Contains(ancestor.OriginalDefinition))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The error for a type that inherits a generated tag accessor yet declares its own
    /// <c>kind</c> member (spec 010 FR-C5): its family lowered to the union in another
    /// assembly, which cannot see this descendant, so the member would override the tag
    /// and break the union's narrowing. <c>null</c> for any other type.
    /// </summary>
    public MetanoDiagnostic? InheritedTagConflict(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;

        if (definition.IsClosed || !DeclaresKindMember(definition))
            return null;

        for (var ancestor = definition.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (TagOf(ancestor) is { } tag)
            {
                return new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.ClosedFamilyTagSuppressed,
                    $"'{definition.Name}' declares a '{KindMemberName}' member, but it inherits the "
                        + $"'{KindMemberName}' discriminant \"{tag}\" of closed family case "
                        + $"'{ancestor.Name}', whose family lowered to a TypeScript union in its own "
                        + $"assembly. Rename the member (or give it [Name]).",
                    definition.Locations.FirstOrDefault()
                );
            }
        }

        return null;
    }

    /// <summary>
    /// A nested type whose module-level name would be the closed type's runtime class
    /// name (<c>JobStatus.Base</c> → <c>JobStatus$Base</c>).
    /// </summary>
    private static bool TakesRuntimeClassName(INamedTypeSymbol closedType) =>
        closedType.GetTypeMembers(RuntimeClassSuffix[1..]).Length > 0;
}
