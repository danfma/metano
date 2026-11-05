using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What holds a name in one of a class's claim tables (spec 013 naming.md §
/// Claim pass): its own static or instance table, or the static side it
/// inherits (step 8).
/// </summary>
internal enum TsClaimedMemberKind
{
    /// <summary>A user-written member or a dispatcher, which keeps its C# name.</summary>
    User,

    /// <summary>A constructor overload's selector symbol.</summary>
    Selector,

    /// <summary>A constructor overload's factory, whose signature FR-026 compares.</summary>
    Factory,

    /// <summary>A method or operator fast path.</summary>
    FastPath,
}

/// <summary>
/// One name of a claim table and what holds it.
/// </summary>
/// <param name="Kind">What the member is.</param>
/// <param name="Description">How a diagnostic names it: <c>the user member
/// 'fromCents'</c>, <c>the factory of 'Money(int cents)'</c>.</param>
/// <param name="DeclaredBy">The emitted name of the class that declares it,
/// which a diagnostic names when a derived class meets it.</param>
/// <param name="Factory">A factory's signature; <c>null</c> for any other
/// kind.</param>
internal sealed record TsClaimedMember(
    TsClaimedMemberKind Kind,
    string Description,
    string DeclaredBy,
    TsFactorySignature? Factory = null
);

/// <summary>
/// A generated factory's TypeScript signature, the part of the static side
/// FR-026 compares, expressed in the type parameters of the class whose static
/// side holds it.
/// </summary>
/// <param name="Declaration">The constructor overload it constructs through
/// (<c>Money(decimal amount)</c>).</param>
/// <param name="TypeParameters">The class type parameters the factory
/// re-declares, with their constraints (FR-028).</param>
/// <param name="Parameters">Its parameter list.</param>
/// <param name="Accessibility">Its overload's accessibility (FR-011).</param>
/// <param name="BindsTypeParameters">Whether a class inherits it through a
/// base construction that fixes a type parameter (<c>IntBox : Box&lt;int&gt;</c>):
/// the inherited factory stays generic, so no factory of the class can
/// redeclare it.</param>
internal sealed record TsFactorySignature(
    string Declaration,
    IReadOnlyList<TsTypeParameter> TypeParameters,
    IReadOnlyList<TsFactoryParameter> Parameters,
    TsAccessibility Accessibility,
    bool BindsTypeParameters = false
);

/// <summary>One parameter of a <see cref="TsFactorySignature"/>.</summary>
internal sealed record TsFactoryParameter(TsType Type, bool CanBeOmitted, bool Rest);
