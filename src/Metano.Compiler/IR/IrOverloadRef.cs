namespace Metano.Compiler.IR;

/// <summary>
/// A stable identity of one overload of a dispatched group (spec 013
/// DECIDED-18): the declaring type, the member's C# name (<c>.ctor</c> for a
/// constructor) and the overload's declaration ordinal in its group. A call
/// site carries it instead of a final name, and each target resolves it
/// through the declaring type's claim results, so a call in any file or
/// package spells the member the way its declaration emitted it.
/// </summary>
/// <param name="DeclaringType">The type that declares the overload.</param>
/// <param name="MemberName">The member's C# name.</param>
/// <param name="Ordinal">The overload's position among the group's declared
/// overloads — for a constructor, its
/// <see cref="IrConstructorDeclaration.DeclarationOrdinal"/>.</param>
public sealed record IrOverloadRef(IrTypeKey DeclaringType, string MemberName, int Ordinal)
{
    /// <summary>The C# name every constructor overload shares.</summary>
    public const string ConstructorName = ".ctor";

    /// <summary>The constructor overload at <paramref name="ordinal"/> of <paramref name="type"/>.</summary>
    public static IrOverloadRef Constructor(IrTypeKey type, int ordinal) =>
        new(type, ConstructorName, ordinal);
}

/// <summary>
/// A type's identity across compilations: the name of the assembly that
/// declares it and its metadata name (<c>Shop.Money</c>, <c>Shop.Box`1</c>,
/// <c>Shop.Outer+Inner</c>), which a target looks the type up by.
/// </summary>
public sealed record IrTypeKey(string AssemblyName, string MetadataName);
