namespace Metano.Compiler.IR;

/// <summary>
/// A type declaration in the IR. Represents the semantic shape of a C# type
/// without prescribing target-specific rendering.
/// Names stay in their original C# casing.
/// </summary>
public abstract record IrTypeDeclaration(
    string Name,
    IrVisibility Visibility,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrAttribute>? Attributes = null
)
{
    /// <summary>
    /// For a class, struct or interface nested in generic types, the type
    /// parameters of those containers, outermost first, which C# lets the nested
    /// type use without declaring them (<c>Gen&lt;T&gt;.Sub</c> reads
    /// <c>T</c>); <c>null</c> when no container is generic.
    /// </summary>
    public IReadOnlyList<IrTypeParameter>? ContainerTypeParameters { get; init; }
}

/// <summary>
/// A class, struct, or record declaration.
/// </summary>
/// <param name="EqualityMembers">For records and structs, the instance members
/// the type itself declares that C#'s value equality compares (a record's
/// synthesized <c>Equals</c>, a struct's <c>ValueType.Equals</c>), in
/// declaration order; <c>null</c> for any other type. Members of a record base are
/// not repeated: like C#'s <c>base.Equals</c>, backends chain to the emitted
/// base's equality / hash code for them.</param>
public sealed record IrClassDeclaration(
    string Name,
    IrVisibility Visibility,
    IrTypeSemantics Semantics,
    IrTypeRef? BaseType = null,
    IReadOnlyList<IrTypeRef>? Interfaces = null,
    IReadOnlyList<IrMemberDeclaration>? Members = null,
    IrConstructorDeclaration? Constructor = null,
    IReadOnlyList<IrTypeDeclaration>? NestedTypes = null,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrAttribute>? Attributes = null,
    IReadOnlyList<IrRecordEqualityMember>? EqualityMembers = null
) : IrTypeDeclaration(Name, Visibility, TypeParameters, Attributes)
{
    /// <summary>
    /// The root members of the interfaces the type implements more than once
    /// (<c>C : IConv&lt;int&gt;, IConv&lt;string&gt;</c>, spec 013 DECIDED-30): each
    /// root name keeps runtime dispatch over the type's implementations of it.
    /// <c>null</c> when the type implements no interface more than once.
    /// </summary>
    public IReadOnlyList<IrConstructedRoot>? ConstructedRoots { get; init; }

    /// <summary>
    /// The body of the type's static constructor (<c>static C() { … }</c>),
    /// which C# runs after the static field initializers; <c>null</c> when the
    /// type declares none, and an empty list for an empty one (which still makes
    /// .NET initialize the type at first use, not <c>beforefieldinit</c>). A
    /// target that can run code at type initialization lowers it; any other
    /// reports it.
    /// </summary>
    public IReadOnlyList<IrStatement>? StaticInitializer { get; init; }

    /// <summary>
    /// Full-arity reified type parameters of this generic class (spec 011 FR-024,
    /// stage 011d); empty when the class has no reified instance type parameters.
    /// </summary>
    public IReadOnlyList<IrReifiedTypeParameter> ReifiedTypeParameters { get; init; } = [];

    /// <summary>
    /// Per-declaring-class slot name (<c>$typeArguments$&lt;Class&gt;</c>) when
    /// this class defines one; <c>null</c> otherwise.
    /// </summary>
    public string? TypeArgumentSlot { get; init; }

    /// <summary>
    /// Base class's full-arity type arguments passed to <c>super</c> when the
    /// base class defines a type-argument slot; <c>null</c> otherwise.
    /// </summary>
    public IReadOnlyList<IrTypeRef>? BaseTypeArguments { get; init; }

    /// <summary>
    /// Whether this type has a public parameterless constructor (for <c>create</c>
    /// emission on its runtime descriptor, spec 011 FR-024).
    /// </summary>
    public bool HasParameterlessCtor { get; init; }

    /// <summary>
    /// The facts behind the record's .NET text (baseline FR-093); <c>null</c> for
    /// a type without record value equality.
    /// </summary>
    public IrRecordPrinting? Printing { get; init; }

    /// <summary>
    /// The constructed root <paramref name="method"/> is anchored on: a branch
    /// of that root's runtime dispatcher, not a fast path; <c>null</c> otherwise.
    /// </summary>
    public IrConstructedRoot? ConstructedRootOf(IrMethodDeclaration method) =>
        method.Anchor?.Root is { } root
            ? ConstructedRoots?.FirstOrDefault(constructed => constructed.Root == root)
            : null;
}

/// <summary>
/// A member of a dispatched group of an interface a type implements more than
/// once (spec 013 FR-036), as that type sees it.
/// </summary>
/// <param name="Root">The interface member's definition, which every
/// implementation is anchored on.</param>
/// <param name="Constructions">Its signature in each construction the type
/// implements (<c>Conv(int x)</c>, <c>Conv(string x)</c>), in the order of the
/// type's interfaces.</param>
/// <param name="IsInherited">Whether an emitted base class already declares the
/// root's name, so the type's runtime dispatcher overrides it and forwards the
/// constructions it does not implement.</param>
public sealed record IrConstructedRoot(
    IrOverloadRef Root,
    IReadOnlyList<IrMethodDeclaration> Constructions,
    bool IsInherited
)
{
    /// <summary>
    /// Per construction, in the order of <see cref="Constructions"/>: the
    /// method that implements it outside the root's runtime dispatcher (an
    /// inherited method, or an override of a base method the type's base does
    /// not dispatch under the root), which the dispatcher reaches by its own
    /// name (#594); <c>null</c> for a construction a method of the type
    /// anchored on the root implements, or that nothing emitted implements.
    /// Empty when <see cref="IsInherited"/>: the base's dispatcher handles
    /// what the type does not implement.
    /// </summary>
    public IReadOnlyList<IrOverloadRef?> Forwards { get; init; } = [];
}

/// <summary>
/// One member that takes part in a record's value equality.
/// </summary>
/// <param name="Name">The C# member name (property, or field when the record
/// declares a plain field).</param>
/// <param name="Type">The member's type — drives the strict vs value
/// comparison choice (<see cref="Metano.Compiler.Analysis.IrEqualityClassifier"/>).</param>
/// <param name="EmittedName">Target-resolved <c>[Name]</c> override, or
/// <c>null</c> to apply the backend's naming policy to
/// <paramref name="Name"/>.</param>
public sealed record IrRecordEqualityMember(string Name, IrTypeRef Type, string? EmittedName);

/// <summary>
/// An interface declaration.
/// </summary>
public sealed record IrInterfaceDeclaration(
    string Name,
    IrVisibility Visibility,
    IReadOnlyList<IrTypeRef>? BaseInterfaces = null,
    IReadOnlyList<IrMemberDeclaration>? Members = null,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrAttribute>? Attributes = null
) : IrTypeDeclaration(Name, Visibility, TypeParameters, Attributes);

/// <summary>
/// An enum declaration.
/// </summary>
public sealed record IrEnumDeclaration(
    string Name,
    IrVisibility Visibility,
    IReadOnlyList<IrEnumMember> Members,
    IrEnumStyle Style = IrEnumStyle.Numeric,
    IReadOnlyList<IrAttribute>? Attributes = null
) : IrTypeDeclaration(Name, Visibility, Attributes: Attributes)
{
    /// <summary>
    /// Whether the enum carries <c>[System.Flags]</c>, so its values are bit
    /// combinations of the members.
    /// </summary>
    public bool IsFlags { get; init; }

    /// <summary>
    /// The exact kind of the enum's underlying integer type (<c>int</c> by
    /// default, <c>long</c> for <c>enum E : long</c>).
    /// </summary>
    public IrNumericKind UnderlyingKind { get; init; } = IrNumericKind.Int32;
}

/// <summary>
/// A delegate declaration — its signature lowers to a TS type alias
/// (<c>export type Name = (…) =&gt; R;</c>) on backends that support
/// named function aliases.
/// </summary>
public sealed record IrDelegateDeclaration(
    string Name,
    IrVisibility Visibility,
    IReadOnlyList<IrParameter> Parameters,
    IrTypeRef ReturnType,
    IrTypeRef? ThisType = null,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrAttribute>? Attributes = null
) : IrTypeDeclaration(Name, Visibility, TypeParameters, Attributes);
