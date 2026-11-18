using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The Dart rendering of the runtime-type nodes of spec 011 stage 011b
/// (<see cref="IrGetTypeExpression"/>, <see cref="IrTypeOfExpression"/>):
/// their Dart mapping is deferred to after 3.0.0 (D28), so they lower to the
/// <c>MS0001</c> placeholder.
/// </summary>
internal static class DartRuntimeTypePlaceholder
{
    public static IrExpression Of(string kind)
    {
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{kind}' has no Dart rendering yet (spec 011 D28); the expression throws at run "
                    + "time."
            )
        );

        return new IrUnsupportedExpression(kind);
    }
}
