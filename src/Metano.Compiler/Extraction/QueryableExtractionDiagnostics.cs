using System.ComponentModel;
using Metano.Compiler.Diagnostics;

namespace Metano.Compiler.Extraction;

/// <summary>
/// AsyncLocal sink for diagnostics raised by the queryable-tree
/// walker (<see cref="IrExpressionTreeExtractor"/>). Opened by the
/// host (target transformer / orchestrator) for the duration of
/// extraction so MS0024 reports from the walker can surface without
/// threading a diagnostics list through every static extractor
/// signature.
///
/// <para>
/// <see cref="Open"/> takes an <see cref="Action{T}"/> delegate so
/// the caller can route diagnostics through whatever concurrency
/// discipline they already use (e.g., the TypeScript transformer's
/// <c>_diagnosticsLock</c>). Holding a bare <see cref="List{T}"/>
/// here would mean two different monitor objects guarding the same
/// non-thread-safe list across the parallel per-group transform
/// — the delegate funnels every write through the transformer's
/// single sink.
/// </para>
/// <para>
/// Nesting is supported: <see cref="Open"/> captures the previous
/// slot value and <see cref="IDisposable.Dispose"/> restores it,
/// so an outer scope is not clobbered by an inner one. Today only
/// one scope is open at a time (the per-target <c>TransformAll</c>
/// loop), but the contract holds for any future host that wraps
/// multiple targets.
/// </para>
/// </summary>
/// <remarks>
/// Public surface is a host-extension seam, not an application API.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class QueryableExtractionDiagnostics
{
    private static readonly AsyncLocal<Action<MetanoDiagnostic>?> Sink = new();

    public static IDisposable Open(Action<MetanoDiagnostic> destination)
    {
        var previous = Sink.Value;
        Sink.Value = destination;
        return new Scope(previous);
    }

    public static void Report(MetanoDiagnostic diagnostic) => Sink.Value?.Invoke(diagnostic);

    private sealed class Scope(Action<MetanoDiagnostic>? previous) : IDisposable
    {
        public void Dispose() => Sink.Value = previous;
    }
}
