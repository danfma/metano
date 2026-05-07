using System.Collections.Concurrent;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Aggregates the per-compilation mutable state that <see cref="TypeMapper"/> needs during
/// a transpilation run. Replaces the five <c>[ThreadStatic]</c> fields that were previously
/// used as an implicit side-channel.
///
/// <para>
/// <see cref="CrossPackageMisses"/> and <see cref="UsedCrossPackages"/> are concurrent
/// because the file-group transformation loop fans out across worker threads
/// (#21 — parallel TypeTransformer); resolver helpers in
/// <c>IrTypeOriginResolverFactory</c> and <c>ImportCollector</c> populate both
/// from inside parallel iterations and the post-loop drain in <c>TypeTransformer</c>
/// reads them once at the end.
/// </para>
/// </summary>
public sealed class TypeMappingContext(
    IReadOnlyDictionary<string, IrBclExport> bclExportMap,
    IReadOnlyDictionary<string, IrTypeOrigin> crossAssemblyOrigins,
    IReadOnlySet<string> assembliesNeedingEmitPackage,
    ICollection<string>? crossPackageMisses = null,
    IDictionary<string, string>? usedCrossPackages = null
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
            new Dictionary<string, IrTypeOrigin>(),
            new HashSet<string>(StringComparer.Ordinal)
        );

    public IReadOnlyDictionary<string, IrBclExport> BclExportMap { get; } = bclExportMap;

    /// <summary>
    /// Cross-assembly origins indexed by <see cref="SymbolHelper.GetStableFullName"/>.
    /// Carried directly from <see cref="IrCompilation.CrossAssemblyOrigins"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IrTypeOrigin> CrossAssemblyOrigins { get; } =
        crossAssemblyOrigins;

    /// <summary>
    /// Names of referenced assemblies that opted into transpilation but did not declare
    /// <c>[EmitPackage]</c> for the active target. Carried from
    /// <see cref="IrCompilation.AssembliesNeedingEmitPackage"/>.
    /// </summary>
    public IReadOnlySet<string> AssembliesNeedingEmitPackage { get; } =
        assembliesNeedingEmitPackage;

    /// <summary>
    /// Cross-package origins the resolver could not match. Backed by a
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> keyed by FQN so writes
    /// from parallel worker threads stay safe; the value slot is unused
    /// (the dictionary is consumed as a set).
    /// </summary>
    public ICollection<string> CrossPackageMisses { get; } =
        crossPackageMisses ?? new ConcurrentDictionary<string, byte>(StringComparer.Ordinal).Keys;

    /// <summary>
    /// Cross-package npm dependencies the run touched, keyed by package name and
    /// pre-formatted to a version specifier. <see cref="ConcurrentDictionary{TKey, TValue}"/>
    /// so the resolver helpers and import collector can populate it from
    /// parallel worker threads.
    /// </summary>
    public IDictionary<string, string> UsedCrossPackages { get; } =
        usedCrossPackages ?? new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
}
