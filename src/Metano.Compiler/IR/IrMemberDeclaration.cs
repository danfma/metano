namespace Metano.Compiler.IR;

/// <summary>
/// A member of a type declaration. Names stay in their original C# casing —
/// each target backend applies its own naming policy. Member-level attributes
/// (e.g., <c>[Name("x")]</c>, <c>[Ignore]</c>) are carried on <see cref="Attributes"/>.
/// </summary>
public abstract record IrMemberDeclaration(string Name, IrVisibility Visibility, bool IsStatic)
{
    public IReadOnlyList<IrAttribute>? Attributes { get; init; }
}

/// <summary>
/// A method declaration.
/// </summary>
public sealed record IrMethodDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IReadOnlyList<IrParameter> Parameters,
    IrTypeRef ReturnType,
    IReadOnlyList<IrStatement>? Body,
    IrMethodSemantics Semantics,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrMethodDeclaration>? Overloads = null
) : IrMemberDeclaration(Name, Visibility, IsStatic)
{
    /// <summary>
    /// The overload's name in its dispatched group (spec 013 FR-001);
    /// <c>null</c> when the method is not part of one.
    /// </summary>
    public IrOverloadName? OverloadName { get; init; }

    /// <summary>
    /// The overload's C# signature (<c>From(decimal amount)</c>), which a
    /// target's diagnostics name it by; <c>null</c> outside a dispatched group.
    /// </summary>
    public string? SignatureText { get; init; }

    /// <summary>
    /// The overload's position among the methods its declaring type declares
    /// under its C# name (<see cref="IrOverloadRef.Ordinal"/>, spec 013
    /// DECIDED-18); <c>0</c> outside a dispatched group.
    /// </summary>
    public int DeclarationOrdinal { get; init; }

    /// <summary>
    /// The root declaration whose names the overload reuses (spec 013 FR-025):
    /// the overridden method or the implemented interface member; <c>null</c>
    /// when the overload is its own root.
    /// </summary>
    public IrOverloadAnchor? Anchor { get; init; }

    /// <summary>
    /// On the primary of a group that an emitted base class already
    /// dispatches: the overloads of the inherited dispatcher, as the deriving
    /// type sees them (type arguments substituted), the ones it cannot see
    /// included (spec 013 FR-030). Signatures only. <c>null</c> when no base
    /// dispatches the group.
    /// </summary>
    public IReadOnlyList<IrMethodDeclaration>? InheritedOverloads { get; init; }
}

/// <summary>
/// How a method overload is anchored on a root declaration it does not
/// declare (spec 013 DECIDED-15, FR-025): its emitted names are the root's.
/// </summary>
/// <param name="Root">The root overload: for an override the first
/// declaration of its override chain, for an implementation the interface
/// member's definition.</param>
/// <param name="Kind">Whether the overload overrides a class member or
/// implements an interface member.</param>
/// <param name="FurtherRoots">Other interface members the same method
/// implements, whose names a target emits as forwarding members
/// (naming.md § Root anchoring); <c>null</c> when there are none.</param>
/// <param name="ReimplementedRoots">For an implementation, the roots
/// (<see cref="Root"/> and further ones) an emitted base class already
/// implements: a <c>new</c> method or an explicit re-implementation, whose
/// names for those roots override the base's; <c>null</c> when there are
/// none.</param>
public sealed record IrOverloadAnchor(
    IrOverloadRef Root,
    IrOverloadAnchorKind Kind,
    IReadOnlyList<IrOverloadRef>? FurtherRoots = null,
    IReadOnlyList<IrOverloadRef>? ReimplementedRoots = null
);

/// <summary>What an anchored overload does to its root.</summary>
public enum IrOverloadAnchorKind
{
    /// <summary>It overrides a class member (<c>override</c>).</summary>
    Override,

    /// <summary>It implements an interface member, implicitly or explicitly.</summary>
    Implementation,
}

/// <summary>
/// A property declaration. Carries per-accessor visibility so shapes like
/// <c>public string Status { get; private set; }</c> are representable.
/// </summary>
public sealed record IrPropertyDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IrTypeRef Type,
    IrPropertyAccessors Accessors,
    IrVisibility? SetterVisibility = null,
    IrExpression? Initializer = null,
    IReadOnlyList<IrStatement>? GetterBody = null,
    IReadOnlyList<IrStatement>? SetterBody = null,
    IrPropertySemantics? Semantics = null,
    bool IsOptional = false
) : IrMemberDeclaration(Name, Visibility, IsStatic);

/// <summary>
/// A field declaration.
/// </summary>
/// <param name="IsCapturedByCtor">When <c>true</c>, this field's
/// <see cref="Initializer"/> is a bare identifier that names a constructor
/// parameter (DI-style: <c>private readonly IFoo _foo = foo;</c>). The
/// matching <see cref="IrConstructorParameter.CapturedFieldName"/> carries
/// the back-reference. Backends emit the <c>this._foo = foo</c> assignment
/// in the constructor body and skip the field's initializer at the same
/// time so the value is assigned exactly once.</param>
/// <param name="IsRequired">Declared <c>required</c>: every object creation sets it
/// through an object initializer (or a <c>[SetsRequiredMembers]</c> constructor).</param>
/// <param name="IsAssignedByInitAccessor">A <c>readonly</c> field that a transpiled
/// <c>init</c> accessor body of its type assigns (see <c>CustomInitAccessor</c>). C#
/// allows that write; a backend that lowers the accessor to a setter, which cannot
/// write a read-only field, declares the field mutable.</param>
/// <param name="IsCompileTimeConstant">Declared with the C# <c>const</c> modifier
/// (Roslyn's <c>IFieldSymbol.IsConst</c>): implies <see cref="IsReadonly"/> (a C#
/// <c>const</c> is read-only). Distinct from <see cref="IsConstant"/>, which is the
/// <c>[Constant]</c> attribute, and from
/// <see cref="IrVariableDeclaration.IsConst"/>, which only means "never
/// reassigned"; a backend may keep such a field's source name (a module-scope
/// constant is the module's public shape, #390).</param>
public sealed record IrFieldDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IrTypeRef Type,
    bool IsReadonly,
    IrExpression? Initializer = null,
    bool IsCapturedByCtor = false,
    bool IsConstant = false,
    bool IsRequired = false,
    bool IsAssignedByInitAccessor = false,
    bool IsCompileTimeConstant = false
) : IrMemberDeclaration(Name, Visibility, IsStatic);

/// <summary>
/// An event declaration.
/// </summary>
public sealed record IrEventDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IrTypeRef HandlerType
) : IrMemberDeclaration(Name, Visibility, IsStatic);
