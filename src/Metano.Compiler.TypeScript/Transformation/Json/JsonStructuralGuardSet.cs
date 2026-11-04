using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The types whose guard is synthesized (original definitions) and their
/// cross-assembly origin keys, which join the guardable type keys so guard
/// references resolve to them.
/// </summary>
internal sealed record JsonStructuralGuardSet(
    IReadOnlySet<INamedTypeSymbol> Types,
    IReadOnlySet<string> OriginKeys
);
