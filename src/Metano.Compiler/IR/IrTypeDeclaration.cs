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
);

/// <summary>
/// A class, struct, or record declaration.
/// </summary>
/// <param name="EqualityMembers">For records, the instance members the record
/// itself declares that C#'s synthesized value equality compares, in
/// declaration order; <c>null</c> for non-records. Members of a record base are
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
);

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
) : IrTypeDeclaration(Name, Visibility, Attributes: Attributes);

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
