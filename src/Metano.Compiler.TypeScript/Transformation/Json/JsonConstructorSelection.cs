using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The constructor a <c>TypeSpec</c>'s <c>factory</c> builds a class with, and
/// how it reaches it (spec 013 DECIDED-25, FR-035). A type that constructs
/// through its overload factories uses the overload System.Text.Json picks —
/// the one marked <c>[JsonConstructor]</c>, now that every overload is
/// addressable — through that overload's factory. Every other type keeps the
/// constructor <see cref="IrConstructorExtractor.FindPrimaryConstructor"/>
/// picks, through <c>new</c>; so does a type read from metadata only, whose
/// factory names are unknown (DECIDED-24).
/// </summary>
internal static class JsonConstructorSelection
{
    /// <summary>The constructor the factory of <paramref name="type"/> calls.</summary>
    public static IMethodSymbol? Of(INamedTypeSymbol type) =>
        ConstructsThroughFactories(type) && MarkedConstructorOf(type) is { } marked
            ? marked
            : IrConstructorExtractor.FindPrimaryConstructor(type);

    /// <summary>
    /// The claimed factory that builds the type through
    /// <paramref name="constructor"/>; <c>null</c> when the TypeSpec keeps
    /// <c>new</c>.
    /// </summary>
    public static string? FactoryOf(IMethodSymbol constructor) =>
        DispatchedConstructorCalls.ConstructionTarget(constructor, TargetLanguage.TypeScript)
            is { } overload
            ? TsOverloadCallSites.ConstructorNamesOf(overload)?.Factory
            : null;

    /// <summary>The constructor <paramref name="type"/> marks <c>[JsonConstructor]</c>, if any.</summary>
    public static IMethodSymbol? MarkedConstructorOf(INamedTypeSymbol type) =>
        type.InstanceConstructors.FirstOrDefault(constructor =>
            JsonSerializationAttributes.Has(constructor, JsonSerializationAttributes.Constructor)
        );

    private static bool ConstructsThroughFactories(INamedTypeSymbol type) =>
        DispatchedConstructorCalls.ConstructsThroughFactories(type, TargetLanguage.TypeScript);
}
