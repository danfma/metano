namespace Metano.Compiler.IR;

/// <summary>
/// How a dispatched method group declares its dispatcher (spec 013 FR-030,
/// DECIDED-15).
/// </summary>
public enum IrDispatcherKind
{
    /// <summary>
    /// No dispatcher: the group only overrides overloads whose dispatcher an
    /// emitted base declares, or it is a single anchored overload.
    /// </summary>
    None,

    /// <summary>The group's own dispatcher over its declared overloads.</summary>
    Declared,

    /// <summary>
    /// A dispatcher overriding the inherited one: its signatures are the
    /// inherited dispatcher's plus the overloads the type adds, and the calls
    /// it does not handle go to the base's.
    /// </summary>
    OverridesInherited,
}

/// <summary>
/// One method group as the IR folds it — a primary
/// <see cref="IrMethodDeclaration"/> carrying its siblings in
/// <see cref="IrMethodDeclaration.Overloads"/> — read the way every target
/// dispatches it (spec 013 FR-030).
/// </summary>
public sealed class IrMethodGroup
{
    private IrMethodGroup(IrMethodDeclaration primary)
    {
        Primary = primary;
        Members = [primary, .. primary.Overloads ?? []];
    }

    /// <summary>The method that carries the group.</summary>
    public IrMethodDeclaration Primary { get; }

    /// <summary>Every overload the type declares, the primary first.</summary>
    public IReadOnlyList<IrMethodDeclaration> Members { get; }

    /// <summary>Whether the members are fast paths of a dispatched group (FR-001).</summary>
    public bool IsDispatched => Primary.OverloadName is not null;

    /// <summary>How the group declares its dispatcher.</summary>
    public IrDispatcherKind DispatcherKind =>
        (InheritsDispatcher: Primary.InheritedOverloads is not null, AddsOverloads) switch
        {
            (InheritsDispatcher: true, AddsOverloads: true) => IrDispatcherKind.OverridesInherited,
            (InheritsDispatcher: false, _) when Members.Count >= 2 => IrDispatcherKind.Declared,
            _ => IrDispatcherKind.None,
        };

    /// <summary>Whether a member is more than an override of an inherited overload.</summary>
    private bool AddsOverloads => Members.Any(member => !IsOverride(member));

    /// <summary>
    /// The overloads the dispatcher branches to by their runtime checks, in the
    /// group's order: every member of a declared dispatcher; the overloads a
    /// type adds to an inherited one, whose own branches reach the overrides
    /// through their fast paths; none without a dispatcher. The synthesized
    /// value-equality member and the overloads shadowed by it
    /// (<see cref="IrMethodDeclaration.IsShadowedBySynthesized"/>) dispatch
    /// around them instead (spec 013 FR-042, <see cref="Synthesized"/>,
    /// <see cref="ShadowedBranches"/>).
    /// </summary>
    public IReadOnlyList<IrMethodDeclaration> Branches =>
        DispatcherKind switch
        {
            IrDispatcherKind.Declared => Members.Where(IsCheckedBranch).ToList(),
            IrDispatcherKind.OverridesInherited => Members
                .Where(member => !IsOverride(member) && IsCheckedBranch(member))
                .ToList(),
            _ => [],
        };

    /// <summary>
    /// The synthesized value-equality member of the group (spec 013 FR-038):
    /// its dispatcher tries it first for an argument of the receiver's own type
    /// and again after the checked branches; <c>null</c> when none joined.
    /// </summary>
    public IrMethodDeclaration? Synthesized =>
        Members.FirstOrDefault(member => member.SynthesizedKind is not null);

    /// <summary>
    /// The shadowed overloads that also accept argument counts the synthesized
    /// member does not: their branches follow its last one, reached only at
    /// those counts. A shadowed overload the synthesized member covers
    /// entirely has no branch.
    /// </summary>
    public IReadOnlyList<IrMethodDeclaration> ShadowedBranches =>
        DispatcherKind is IrDispatcherKind.None || Synthesized is not { } synthesized
            ? []
            : Members
                .Where(member =>
                    member.IsShadowedBySynthesized
                    && !IsOverride(member)
                    && !IsCoveredBy(member, synthesized)
                )
                .ToList();

    private bool IsCheckedBranch(IrMethodDeclaration member) =>
        member.SynthesizedKind is null && !member.IsShadowedBySynthesized;

    private static bool IsCoveredBy(IrMethodDeclaration member, IrMethodDeclaration synthesized) =>
        RequiredArityOf(member) >= RequiredArityOf(synthesized)
        && MaximumArityOf(member) <= MaximumArityOf(synthesized);

    /// <summary>The fewest arguments a call passes <paramref name="method"/>.</summary>
    public static int RequiredArityOf(IrMethodDeclaration method) =>
        method.Parameters.Count(parameter => !parameter.CanBeOmitted && !parameter.IsParams);

    /// <summary>The most arguments a call passes <paramref name="method"/>; <see cref="int.MaxValue"/> with <c>params</c>.</summary>
    public static int MaximumArityOf(IrMethodDeclaration method) =>
        method.Parameters.Any(parameter => parameter.IsParams)
            ? int.MaxValue
            : method.Parameters.Count;

    public static IrMethodGroup Of(IrMethodDeclaration primary) => new(primary);

    /// <summary>Whether <paramref name="member"/> overrides a class member it is anchored on.</summary>
    public static bool IsOverride(IrMethodDeclaration member) =>
        member.Anchor is { Kind: IrOverloadAnchorKind.Override };
}
