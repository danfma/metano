using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Reports a construct the Dart prototype cannot emit faithfully (<c>MS0001</c>
/// warning) through the transform's diagnostic sink, which
/// <c>DartTransformer.TransformAll</c> opens for the whole transform.
/// </summary>
internal static class DartUnsupported
{
    public static void Report(string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message
            )
        );
}
