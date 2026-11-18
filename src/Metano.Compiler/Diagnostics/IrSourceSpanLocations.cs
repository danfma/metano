using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Metano.Compiler.Diagnostics;

/// <summary>
/// Converts between a Roslyn <see cref="Location"/> and the Roslyn-free
/// <see cref="IrSourceSpan"/> the IR carries, so a target can report a diagnostic at
/// a source position the core recorded.
/// </summary>
public static class IrSourceSpanLocations
{
    /// <summary>The span of <paramref name="location"/>; <c>null</c> outside source.</summary>
    public static IrSourceSpan? From(Location location)
    {
        if (!location.IsInSource)
            return null;

        var lines = location.GetLineSpan();

        return new IrSourceSpan(
            lines.Path,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            lines.StartLinePosition.Line,
            lines.StartLinePosition.Character,
            lines.EndLinePosition.Line,
            lines.EndLinePosition.Character
        );
    }

    /// <summary>The location <paramref name="span"/> names.</summary>
    public static Location ToLocation(IrSourceSpan span) =>
        Location.Create(
            span.FilePath,
            new TextSpan(span.Start, span.Length),
            new LinePositionSpan(
                new LinePosition(span.StartLine, span.StartCharacter),
                new LinePosition(span.EndLine, span.EndCharacter)
            )
        );
}
