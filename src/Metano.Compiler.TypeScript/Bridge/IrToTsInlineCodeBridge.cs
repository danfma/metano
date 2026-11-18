using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers inline TypeScript (<c>Js.Code</c>, spec 011 FR-031, FR-032) and the
/// escape hatches (<c>UnsafeCast&lt;T&gt;()</c>, <c>AsDynamic()</c>; FR-035,
/// FR-036). In expression position the text is <c>(text) as T</c>, or
/// <c>(text) as any</c> for the untyped <c>Js.Code</c>; in statement position it is
/// statement text that always ends with <c>;</c> unless it already does (a trailing
/// <c>}</c> gets one too: <c>x = () =&gt; {}</c> followed by a line that starts with
/// <c>(</c> would otherwise merge under ASI), on a new line when the text ends
/// inside a <c>//</c> comment. A hole inside a JS string literal of the text
/// reports <c>MS0056</c>.
/// </summary>
internal static class IrToTsInlineCodeBridge
{
    public static TsExpression MapExpression(
        IrInlineCodeExpression inlineCode,
        IrToTsBridgeContext context
    )
    {
        ReportHolesInStringLiterals(InlineCodeLexer.Scan(inlineCode.Parts));

        var resultType = inlineCode.ResultType is { } type
            ? IrToTsTypeMapper.Map(type, context.TypeOverrides)
            : new TsAnyType();

        return new TsCastExpression(
            new TsInlineCode(MapParts(inlineCode.Parts, context)),
            resultType
        );
    }

    public static TsStatement MapStatement(
        IrInlineCodeStatement inlineCode,
        IrToTsBridgeContext context
    )
    {
        var scan = InlineCodeLexer.Scan(inlineCode.Parts);

        ReportHolesInStringLiterals(scan);

        return new TsInlineCodeStatement([
            .. MapParts(inlineCode.Parts, context),
            .. StatementEnding(inlineCode.Parts, scan),
        ]);
    }

    /// <summary><c>value as unknown as T</c>: no check, no helper (FR-035).</summary>
    public static TsExpression MapUnsafeCast(
        IrUnsafeCastExpression unsafeCast,
        IrToTsBridgeContext context
    ) =>
        new TsCastExpression(
            new TsCastExpression(
                IrToTsExpressionBridge.Map(unsafeCast.Value, context),
                new TsNamedType("unknown")
            ),
            IrToTsTypeMapper.Map(unsafeCast.Target, context.TypeOverrides)
        );

    /// <summary><c>(value as any)</c> (FR-036).</summary>
    public static TsExpression MapAsDynamic(
        IrAsDynamicExpression asDynamic,
        IrToTsBridgeContext context
    ) =>
        new TsParenthesized(
            new TsCastExpression(
                IrToTsExpressionBridge.Map(asDynamic.Value, context),
                new TsAnyType()
            )
        );

    private static IReadOnlyList<TsInlineCodePart> MapParts(
        IReadOnlyList<IrInlineCodePart> parts,
        IrToTsBridgeContext context
    ) =>
        [
            .. parts.Select<IrInlineCodePart, TsInlineCodePart>(part =>
                part switch
                {
                    IrInlineCodeHole hole => new TsInlineCodeHole(
                        IrToTsExpressionBridge.Map(hole.Expression, context)
                    ),
                    IrInlineCodeText text => new TsInlineCodeText(text.Text),
                    _ => throw new InvalidOperationException(
                        $"Unknown inline-code part '{part.GetType().Name}'."
                    ),
                }
            ),
        ];

    /// <summary>
    /// What the statement text needs at its end: nothing when its last non-blank
    /// character is already <c>;</c>, a <c>;</c> on a new line when it ends inside a
    /// <c>//</c> comment, else <c>;</c>.
    /// </summary>
    private static IReadOnlyList<TsInlineCodePart> StatementEnding(
        IReadOnlyList<IrInlineCodePart> parts,
        InlineCodeScan scan
    )
    {
        if (scan.EndsInLineComment)
            return [new TsInlineCodeLineBreak(), new TsInlineCodeText(";")];

        return EndsWithSemicolon(parts) ? [] : [new TsInlineCodeText(";")];
    }

    private static bool EndsWithSemicolon(IReadOnlyList<IrInlineCodePart> parts) =>
        parts.LastOrDefault(part =>
            part is IrInlineCodeHole
            || part is IrInlineCodeText { Text: var text } && !string.IsNullOrWhiteSpace(text)
        )
            is IrInlineCodeText last
        && last.Text.TrimEnd().EndsWith(';');

    private static void ReportHolesInStringLiterals(InlineCodeScan scan)
    {
        foreach (var hole in scan.HolesInStringLiterals)
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.InlineCodeHoleInStringLiteral,
                    "This Js.Code hole falls inside a JavaScript string literal of the text: its "
                        + "value is spliced as code, not quoted. Move the hole out of the quotes, "
                        + "or quote the value in C# (JSON.stringify in a hole).",
                    hole.Span is { } span ? IrSourceSpanLocations.ToLocation(span) : null
                )
            );
        }
    }
}
