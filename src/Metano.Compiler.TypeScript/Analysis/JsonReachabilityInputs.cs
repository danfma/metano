using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>
/// What the serializer contexts add to runtime type reachability (spec 011 FR-028, D31):
/// the contract classes their specs construct, and the collection types of their
/// contracts, whose element and argument types the context's descriptors name.
/// </summary>
internal sealed record JsonReachabilityInputs(
    IReadOnlyList<INamedTypeSymbol> ConstructedClasses,
    IReadOnlyList<ITypeSymbol> CollectionTypes
);
