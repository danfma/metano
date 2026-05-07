using System.Collections;
using System.Collections.Concurrent;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Minimal thread-safe set built on top of <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// The .NET BCL does not ship a concurrent <c>HashSet</c>; the dictionary's
/// <c>Keys</c> property is read-only, so it cannot be used directly to back a
/// mutable <see cref="ICollection{T}"/>. Add semantics are <c>TryAdd</c>-style
/// (duplicate inserts are silently ignored).
/// </summary>
internal sealed class ConcurrentHashSet<T>(IEqualityComparer<T>? comparer = null) : ICollection<T>
    where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _store = new(
        comparer ?? EqualityComparer<T>.Default
    );

    public int Count => _store.Count;
    public bool IsReadOnly => false;

    public void Add(T item) => _store.TryAdd(item, 0);

    public void Clear() => _store.Clear();

    public bool Contains(T item) => _store.ContainsKey(item);

    public void CopyTo(T[] array, int arrayIndex) => _store.Keys.CopyTo(array, arrayIndex);

    public bool Remove(T item) => _store.TryRemove(item, out _);

    public IEnumerator<T> GetEnumerator() => _store.Keys.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

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
    /// Cross-package origins the resolver could not match. Defaults to a
    /// <see cref="ConcurrentHashSet{T}"/> so writes from parallel worker
    /// threads stay safe.
    /// </summary>
    public ICollection<string> CrossPackageMisses { get; } =
        crossPackageMisses ?? new ConcurrentHashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Cross-package npm dependencies the run touched, keyed by package name and
    /// pre-formatted to a version specifier. <see cref="ConcurrentDictionary{TKey, TValue}"/>
    /// so the resolver helpers and import collector can populate it from
    /// parallel worker threads.
    /// </summary>
    public IDictionary<string, string> UsedCrossPackages { get; } =
        usedCrossPackages ?? new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
}
