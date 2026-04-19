using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Transformation;

/// <summary>
/// Aggregates the per-compilation mutable state that <see cref="TypeMapper"/> needs during
/// a transpilation run. Replaces the five <c>[ThreadStatic]</c> fields that were previously
/// used as an implicit side-channel.
///
/// This object is <em>mutable</em> because <see cref="UsedCrossPackages"/> and
/// <see cref="CrossPackageMisses"/> accumulate data during transformation.
/// </summary>
public sealed class TypeMappingContext(
    IReadOnlyDictionary<string, IrBclExport> bclExportMap,
    Dictionary<ISymbol, CrossAssemblyEntry> crossAssemblyTypeMap,
    HashSet<IAssemblySymbol> assembliesNeedingEmitPackage,
    HashSet<string>? crossPackageMisses = null,
    Dictionary<string, string>? usedCrossPackages = null
)
{
    /// <summary>
    /// An empty context for non-pipeline callers (e.g., unit tests that exercise
    /// <see cref="TypeMapper"/> in isolation). Type mapping will still work for
    /// primitives and Roslyn built-in types; BCL exports and cross-assembly origins will
    /// simply not resolve.
    /// </summary>
    public static TypeMappingContext Empty { get; } =
        new(
            new Dictionary<string, IrBclExport>(),
            new(SymbolEqualityComparer.Default),
            new(SymbolEqualityComparer.Default)
        );

    public IReadOnlyDictionary<string, IrBclExport> BclExportMap { get; } = bclExportMap;

    public Dictionary<ISymbol, CrossAssemblyEntry> CrossAssemblyTypeMap { get; } =
        crossAssemblyTypeMap;

    public HashSet<IAssemblySymbol> AssembliesNeedingEmitPackage { get; } =
        assembliesNeedingEmitPackage;

    public HashSet<string> CrossPackageMisses { get; } = crossPackageMisses ?? [];

    public Dictionary<string, string> UsedCrossPackages { get; } = usedCrossPackages ?? [];
}
