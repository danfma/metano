using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Metano.Compiler.Caching;

/// <summary>
/// A diagnostic the incremental cache persists, so a build that reuses the previous output
/// reports the same warnings again instead of only the build that produced them. Keeps what
/// <see cref="MetanoDiagnostic.Format"/> prints, which is the only thing a replay needs.
/// </summary>
/// <param name="Severity">The <see cref="MetanoDiagnosticSeverity"/> name.</param>
/// <param name="Path">The source file of the location, or <see langword="null"/> without one.</param>
/// <param name="Line">Zero-based start line of the location.</param>
/// <param name="Column">Zero-based start column of the location.</param>
public sealed record CachedDiagnostic(
    string Severity,
    string Code,
    string Message,
    string? Path = null,
    int Line = 0,
    int Column = 0
)
{
    public static CachedDiagnostic From(MetanoDiagnostic diagnostic)
    {
        var span = diagnostic.Location?.GetLineSpan();
        var hasLocation = span is { IsValid: true };

        return new CachedDiagnostic(
            diagnostic.Severity.ToString(),
            diagnostic.Code,
            diagnostic.Message,
            hasLocation ? span!.Value.Path : null,
            hasLocation ? span!.Value.StartLinePosition.Line : 0,
            hasLocation ? span!.Value.StartLinePosition.Character : 0
        );
    }

    /// <summary>The diagnostic as a run reports it, or <see langword="null"/> for an unknown severity.</summary>
    public MetanoDiagnostic? ToDiagnostic()
    {
        if (!Enum.TryParse<MetanoDiagnosticSeverity>(Severity, out var severity))
            return null;

        Location? location = null;

        if (Path is not null)
        {
            var position = new LinePosition(Line, Column);
            location = Location.Create(Path, default, new LinePositionSpan(position, position));
        }

        return new MetanoDiagnostic(severity, Code, Message, location);
    }

    public static IReadOnlyList<CachedDiagnostic> FromAll(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) =>
        [
            .. diagnostics
                .Where(diagnostic => diagnostic.Severity != MetanoDiagnosticSeverity.Error)
                .Select(From),
        ];

    public static IReadOnlyList<MetanoDiagnostic> ToAll(IEnumerable<CachedDiagnostic>? cached) =>
        [.. (cached ?? []).Select(entry => entry.ToDiagnostic()).OfType<MetanoDiagnostic>()];
}
