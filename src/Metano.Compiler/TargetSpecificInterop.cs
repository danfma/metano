using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// Reads the <c>[TargetSpecific(language)]</c> marker the <c>Metano</c> package puts
/// on its interop APIs that exist for one target language only
/// (<c>Metano.Interop.Js</c>; spec 011 FR-033). The marker is internal to that
/// package, so it is matched by its full metadata name.
/// </summary>
public static class TargetSpecificInterop
{
    /// <summary>The full metadata name of the marker attribute.</summary>
    public const string AttributeName = "Metano.Interop.TargetSpecificAttribute";

    /// <summary>
    /// The target language <paramref name="symbol"/> is bound to: its own marker,
    /// else the nearest marker of its containing types (a member of <c>Js</c> is
    /// TypeScript's because <c>Js</c> is). <c>null</c> for a symbol every target
    /// may use.
    /// </summary>
    public static TargetLanguage? LanguageOf(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            if (DeclaredLanguageOf(current) is { } language)
                return language;
        }

        return null;
    }

    private static TargetLanguage? DeclaredLanguageOf(ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == AttributeName)
            .Select(attribute => attribute.ConstructorArguments[0].Value)
            .OfType<int>()
            .Select(value => (TargetLanguage?)(TargetLanguage)value)
            .FirstOrDefault();
}
