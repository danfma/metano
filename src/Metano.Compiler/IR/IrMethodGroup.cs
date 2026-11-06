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
    /// The overloads the dispatcher branches to, in the group's order: every
    /// member of a declared dispatcher; the overloads a type adds to an
    /// inherited one, whose own branches reach the overrides through their
    /// fast paths; none without a dispatcher.
    /// </summary>
    public IReadOnlyList<IrMethodDeclaration> Branches =>
        DispatcherKind switch
        {
            IrDispatcherKind.Declared => Members,
            IrDispatcherKind.OverridesInherited => Members
                .Where(member => !IsOverride(member))
                .ToList(),
            _ => [],
        };

    public static IrMethodGroup Of(IrMethodDeclaration primary) => new(primary);

    /// <summary>Whether <paramref name="member"/> overrides a class member it is anchored on.</summary>
    public static bool IsOverride(IrMethodDeclaration member) =>
        member.Anchor is { Kind: IrOverloadAnchorKind.Override };
}
