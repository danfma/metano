using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Inline TypeScript (<c>Js.Code</c>) has no Dart rendering (spec 011 D28: a new IR
/// node may lower to an <c>MS0001</c> placeholder until Dart is mapped). The
/// placeholder reports <c>MS0001</c> itself, without relying on <c>MS0051</c>
/// having fired, and lowers to the throwing unsupported expression.
/// </summary>
internal static class DartInlineCodePlaceholder
{
    private const string Kind = "InlineCode";

    public static IrExpression Of(IrSourceSpan? span)
    {
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                "Js.Code is inline TypeScript and has no Dart rendering; the call throws at run "
                    + "time. Mark the containing member [Ignore(TargetLanguage.Dart)].",
                span is null ? null : IrSourceSpanLocations.ToLocation(span)
            )
        );

        return new IrUnsupportedExpression(Kind);
    }
}
