using Metano.Compiler.Diagnostics;

namespace Metano.Compiler.Extraction;

/// <summary>
/// AsyncLocal sink for diagnostics raised by the queryable-tree
/// walker (<see cref="IrExpressionTreeExtractor"/>). Established
/// once at the top of an extraction run by the frontend and read
/// at the bottom, so MS0024 can surface without threading a
/// diagnostics list through every static extractor signature.
///
/// <para>
/// Scoped via the <see cref="Open"/> helper which returns an
/// <see cref="IDisposable"/> — the using-block guarantees the
/// slot is cleared even if extraction throws, so a residual
/// list cannot leak into the next run.
/// </para>
/// </summary>
internal static class QueryableExtractionDiagnostics
{
    private static readonly AsyncLocal<List<MetanoDiagnostic>?> Sink = new();

    public static IDisposable Open(List<MetanoDiagnostic> destination)
    {
        Sink.Value = destination;
        return new Scope();
    }

    public static void Report(MetanoDiagnostic diagnostic)
    {
        var list = Sink.Value;
        if (list is null)
            return;
        lock (list)
        {
            list.Add(diagnostic);
        }
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Sink.Value = null;
    }
}
