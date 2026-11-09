using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The C# declaration a target's claim-pass finding is about (spec 013
/// FR-004, FR-034): a constructor by its declaration ordinal, otherwise a
/// member by its C# name — a method overload also by its signature text
/// (<see cref="OverloadSignatureText"/>), since the overloads of a group may
/// share every parameter name. Every target's claim pass locates its
/// <c>MS0067</c> / <c>MS0068</c> reports through it.
/// </summary>
public sealed record OverloadDeclarationSite(
    string MemberName,
    int? ConstructorOrdinal = null,
    string? Signature = null
)
{
    /// <summary>
    /// The declaration's location in <paramref name="type"/>: the constructor
    /// at its declaration ordinal, the method overload with its signature
    /// text, or the first member with its C# name; the type's when none
    /// matches.
    /// </summary>
    public Location? LocationIn(INamedTypeSymbol type)
    {
        var declaration = ConstructorOrdinal is { } ordinal
            ? ConstructorAt(type, ordinal)
            : MemberIn(type);

        return (declaration ?? type).Locations.FirstOrDefault();
    }

    private static IMethodSymbol ConstructorAt(INamedTypeSymbol type, int ordinal) =>
        IrConstructorExtractor.AllExplicitConstructors(type).ElementAtOrDefault(ordinal)
        ?? throw new InvalidOperationException(
            $"'{type.Name}' has no declared constructor at ordinal {ordinal}."
        );

    private ISymbol? MemberIn(INamedTypeSymbol type)
    {
        var members = type.GetMembers(MemberName);

        if (Signature is not { } signature)
            return members.FirstOrDefault();

        return members
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => OverloadSignatureText.Of(method) == signature);
    }
}
