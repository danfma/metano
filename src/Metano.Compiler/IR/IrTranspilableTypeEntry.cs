using Microsoft.CodeAnalysis;

namespace Metano.Compiler.IR;

/// <summary>
/// Ordered element of <see cref="IrCompilation.TranspilableTypeEntries"/>.
/// Carries the Roslyn <see cref="INamedTypeSymbol"/> of a transpilable
/// top-level type in the current assembly plus the metadata the target
/// needs to route emission without re-walking syntax trees. The
/// <see cref="Symbol"/> reference is a documented escape hatch during
/// the frontend / core split — body extraction, nested-type walking,
/// and semantic-model-backed bridges on the target side still need a
/// symbol in hand. Retiring it requires pre-extracting the full
/// per-type IR up-front, well beyond the scope of this refactor.
/// </summary>
/// <param name="Symbol">The current-assembly top-level type. Nested
/// types are handled as companion namespaces by the target's per-type
/// emission loop — they never appear here on their own.</param>
/// <param name="Key">Assembly-qualified stable full name
/// (<see cref="SymbolHelper.GetCrossAssemblyOriginKey"/>). Matches the
/// <see cref="IrTranspilableTypeRef.Key"/> associated with the same
/// type in <see cref="IrCompilation.TranspilableTypes"/>.</param>
/// <param name="IsSyntheticProgram">True when this entry is the
/// compiler-synthesized containing type for C# 9+ top-level statements.
/// Forward-compat metadata: current targets decide top-level-statement
/// routing by comparing <paramref name="Symbol"/> against
/// <see cref="IrCompilation.EntryPoint"/>'s <c>ContainingType</c>, but
/// the flag lets future consumers route on the entry itself without a
/// symbol-equality probe. Under traditional <c>static void Main</c> the
/// flag is false and the type emits as a regular class.</param>
public sealed record IrTranspilableTypeEntry(
    INamedTypeSymbol Symbol,
    string Key,
    bool IsSyntheticProgram
);
