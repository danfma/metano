using System.ComponentModel;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The severity rule for placeholders (wave 9, W9-13): a construct whose
/// emission replaces executable code with a placeholder — a
/// <c>/* TODO … */</c> marker, a dropped statement or expression, a read that
/// yields <c>undefined</c> instead of the value — is known to be wrong, so it
/// reports MS0001 as an <b>error</b>. A placeholder that still preserves the
/// .NET behaviour or fails loudly at run time (the throwing expression
/// placeholder) keeps the MS0001 warning. Every placeholder emission point of
/// the core and the targets reports through here, so the rule lives in one
/// place.
/// </summary>
/// <remarks>
/// Reports through <see cref="ExtractionDiagnostics"/>; the host keeps a sink
/// open while it extracts and emits. Public surface is a host-extension seam,
/// not an application API.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DroppedCodeDiagnostics
{
    /// <summary>Reports <paramref name="message"/> as an MS0001 error.</summary>
    public static void Report(string message, Location? location = null) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                message,
                location
            )
        );

    /// <summary>
    /// Reports a statement no target can emit, at the statement, and returns
    /// the placeholder that stands for it in the IR.
    /// </summary>
    public static IrUnsupportedStatement UnsupportedStatement(
        SyntaxNode statement,
        string? message = null
    )
    {
        var kind = statement.Kind().ToString();

        Report(
            message ?? $"Statement '{kind}' is not supported by the transpiler.",
            statement.GetLocation()
        );

        return new IrUnsupportedStatement(kind);
    }
}
