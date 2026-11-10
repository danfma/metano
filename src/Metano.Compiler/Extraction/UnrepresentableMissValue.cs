using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports a lookup whose miss value C# defines as <c>default(T)</c> for a
/// struct no target-agnostic literal spells (a user struct, a tuple, a
/// <c>struct</c>-constrained type parameter): the lowered call returns
/// <c>null</c> there (#601).
/// </summary>
internal static class UnrepresentableMissValue
{
    public static void Report(SyntaxNode call, IMethodSymbol method, ITypeSymbol elementType) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'default({elementType.ToDisplayString()})' has no literal in the target: when no "
                    + $"element qualifies, '{method.Name}' returns null instead of the zeroed struct.",
                call.GetLocation()
            )
        );
}
