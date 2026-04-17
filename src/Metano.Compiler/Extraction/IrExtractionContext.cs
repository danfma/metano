using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Context for IR extraction from a Roslyn compilation. Carries the compilation,
/// the current assembly, the assembly-wide transpile flag, and an optional
/// <see cref="IrTypeOriginResolver"/> used to stamp cross-package origins on
/// <see cref="IR.IrNamedTypeRef"/>s.
/// </summary>
public sealed class IrExtractionContext(
    Compilation compilation,
    IAssemblySymbol? currentAssembly = null,
    bool assemblyWideTranspile = false,
    IrTypeOriginResolver? originResolver = null
)
{
    public Compilation Compilation { get; } = compilation;
    public IAssemblySymbol? CurrentAssembly { get; } = currentAssembly;
    public bool AssemblyWideTranspile { get; } = assemblyWideTranspile;

    /// <summary>
    /// Resolves <see cref="IR.IrTypeOrigin"/> for named types coming from other
    /// transpilable assemblies. Null when no cross-package lookup is available
    /// (e.g., in unit tests that exercise the extractors directly).
    /// </summary>
    public IrTypeOriginResolver? OriginResolver { get; } = originResolver;
}
