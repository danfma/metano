using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Metano.Compiler.Diagnostics;

/// <summary>
/// Turns the neutral <see cref="IrSourceLocation"/> an IR node carries back
/// into the Roslyn <see cref="Location"/> a <see cref="MetanoDiagnostic"/>
/// reports, for a target that reports a diagnostic while it lowers the node.
/// </summary>
public static class SourceLocations
{
    public static Location ToRoslyn(IrSourceLocation location) =>
        Location.Create(
            location.FilePath,
            new TextSpan(location.Start, location.Length),
            new LinePositionSpan(
                new LinePosition(location.StartLine, location.StartColumn),
                new LinePosition(location.EndLine, location.EndColumn)
            )
        );
}
