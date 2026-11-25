using System.ComponentModel;
using System.Text.RegularExpressions;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The severity rule for placeholders (wave 9, W9-13 / W9-13a): a construct
/// whose emission replaces executable code with a placeholder — a
/// <c>/* TODO … */</c> marker, a dropped statement or expression, a read that
/// yields <c>undefined</c> instead of the value — is known to be wrong, so it
/// reports MS0001 with the severity of the active target's
/// <see cref="DroppedCodePolicy"/>: an error on TypeScript, a warning on the
/// Dart prototype. A placeholder that still preserves the .NET behaviour or
/// fails loudly at run time (the throwing expression placeholder) keeps the
/// MS0001 warning and does not report through here. Every placeholder
/// emission point of the core and the targets reports through here, so the
/// rule lives in one place.
/// <para>
/// A report names the C# construct and carries its source location: given
/// explicitly, or the location of the statement being emitted
/// (<see cref="At"/>), for a target that renders the IR after extraction.
/// </para>
/// </summary>
/// <remarks>
/// Reports through <see cref="ExtractionDiagnostics"/>; the host keeps a sink
/// open while it extracts and emits. Public surface is a host-extension seam,
/// not an application API.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class DroppedCodeDiagnostics
{
    private static readonly AsyncLocal<DroppedCodePolicy?> ActivePolicy = new();

    private static readonly AsyncLocal<IrSourceSpan?> CurrentStatement = new();

    private static DroppedCodePolicy Policy => ActivePolicy.Value ?? DroppedCodePolicy.Default;

    /// <summary>Makes <paramref name="policy"/> the active target's until the scope is disposed.</summary>
    public static IDisposable Use(DroppedCodePolicy policy)
    {
        var previous = ActivePolicy.Value;

        ActivePolicy.Value = policy;

        return new Restore(() => ActivePolicy.Value = previous);
    }

    /// <summary>
    /// Makes <paramref name="statement"/>'s source location the one a report
    /// without its own location carries, until the scope is disposed; a
    /// statement the core synthesized (no location) keeps the enclosing one.
    /// </summary>
    public static IDisposable At(IrStatement statement)
    {
        if (statement.Span is not { } span)
            return Restore.Nothing;

        var previous = CurrentStatement.Value;

        CurrentStatement.Value = span;

        return new Restore(() => CurrentStatement.Value = previous);
    }

    /// <summary>
    /// Reports <paramref name="message"/> as MS0001 with the active policy's
    /// severity, at <paramref name="location"/> or the current statement.
    /// </summary>
    public static void Report(string message, Location? location = null) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                Policy.Severity,
                DiagnosticCodes.UnsupportedFeature,
                message,
                location ?? CurrentLocation()
            )
        );

    /// <summary>
    /// Reports a statement no target can emit, at the statement, and returns
    /// the placeholder that stands for it in the IR.
    /// </summary>
    public static IrUnsupportedStatement UnsupportedStatement(SyntaxNode statement)
    {
        var kind = statement.Kind();

        Report(UnsupportedStatementMessage(kind), statement.GetLocation());

        return new IrUnsupportedStatement(kind.ToString());
    }

    /// <summary>
    /// The C# construct an IR node stands for, for a target that has no form
    /// for it: <c>IrForStatement</c> is a "for statement",
    /// <c>IrTupleDeconstruction</c> a "tuple deconstruction".
    /// </summary>
    public static string ConstructName(object irNode)
    {
        var name = irNode.GetType().Name;

        return Humanize(name.StartsWith("Ir", StringComparison.Ordinal) ? name[2..] : name);
    }

    private static string UnsupportedStatementMessage(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.GotoStatement =>
                $"'goto' is not supported by the transpiler: {Policy.Language} has no 'goto', so "
                    + "a label used as its target has no equivalent. Use a labeled 'break' or "
                    + "'continue' instead.",
            SyntaxKind.GotoCaseStatement => GotoSectionMessage("goto case"),
            SyntaxKind.GotoDefaultStatement => GotoSectionMessage("goto default"),
            _ => $"The C# {Humanize(kind.ToString())} is not supported by the transpiler.",
        };

    private static string GotoSectionMessage(string jump) =>
        $"'{jump}' is not supported by the transpiler yet: a jump between the sections of a "
        + "switch is not lowered. Move the shared code into a method both sections call.";

    private static Location? CurrentLocation() =>
        CurrentStatement.Value is { } span ? IrSourceSpanLocations.ToLocation(span) : null;

    /// <summary><c>LockStatement</c> → "lock statement".</summary>
    private static string Humanize(string pascalCase) =>
        WordStart().Replace(pascalCase, " $1").Trim().ToLowerInvariant();

    [GeneratedRegex("([A-Z])")]
    private static partial Regex WordStart();

    private sealed class Restore(Action restore) : IDisposable
    {
        public static readonly Restore Nothing = new(static () => { });

        public void Dispose() => restore();
    }
}

/// <summary>
/// How a target treats the placeholders that drop code (W9-13a): the
/// severity of their MS0001 and the language its messages name.
/// </summary>
/// <param name="Severity">Error on a target that ships (TypeScript); warning on a
/// prototype that does not render every body yet (Dart).</param>
/// <param name="Language">The target's language, as the messages name it.</param>
public sealed record DroppedCodePolicy(MetanoDiagnosticSeverity Severity, string Language)
{
    /// <summary>The policy of a host that sets none: dropped code is an error.</summary>
    public static readonly DroppedCodePolicy Default = new(
        MetanoDiagnosticSeverity.Error,
        "the target language"
    );
}
