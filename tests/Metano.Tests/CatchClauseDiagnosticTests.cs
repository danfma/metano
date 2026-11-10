using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// MS0001 for the catch clauses the single-<c>catch</c> lowering cannot keep
/// faithful (#628): a BCL exception type has no class of its own in
/// TypeScript (generated code constructs every BCL exception as a plain
/// <c>Error</c>), so a clause on one
/// cannot come ahead of a clause for another exception type; a clause on a
/// type parameter has no runtime test; and a filter cannot declare a
/// variable or pass an <c>out</c> argument.
/// </summary>
public class CatchClauseDiagnosticTests
{
    private const string SharedRuntimeClassReason =
        "cannot be lowered ahead of a clause for another exception type: its exception type is "
        + "not transpiled, so the target has no class of its own to test (generated code "
        + "constructs every BCL exception as a plain 'Error') and the clause tests "
        + "'instanceof Error', which also takes the exceptions the later clauses handle. "
        + "Telling such types apart is tracked in #651.";

    [Test]
    public async Task BclClauseAheadOfAnotherBclType_IsReportedAtTheClause()
    {
        var warning = CollectUnsupported(
                """
                try { Work(); }
                catch (InvalidOperationException) { Note(1); }
                catch (OverflowException) { Note(2); }
                """
            )
            .Single();

        await Assert
            .That(warning.Message)
            .IsEqualTo($"Catch clause '(InvalidOperationException)' {SharedRuntimeClassReason}");

        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(ReportedText(warning)).IsEqualTo("(InvalidOperationException)");

        await Assert
            .That(ReportedLineText(warning))
            .IsEqualTo("catch (InvalidOperationException) { Note(1); }");
    }

    [Test]
    public async Task BclClauseAheadOfACatchAll_IsReported()
    {
        var warning = CollectUnsupported(
                """
                try { Work(); }
                catch (FormatException e) { Note(e.Message.Length); }
                catch (Exception) { Note(2); }
                """
            )
            .Single();

        await Assert
            .That(warning.Message)
            .IsEqualTo($"Catch clause '(FormatException e)' {SharedRuntimeClassReason}");

        await Assert.That(ReportedText(warning)).IsEqualTo("(FormatException e)");
    }

    [Test]
    public async Task BclClauseAheadOfATranspiledType_IsReported()
    {
        var warning = CollectUnsupported(
                """
                try { Work(); }
                catch (InvalidOperationException) when (Ready) { Note(1); }
                catch (AppException) { Note(2); }
                """
            )
            .Single();

        await Assert.That(ReportedText(warning)).IsEqualTo("(InvalidOperationException)");
    }

    [Test]
    public async Task EveryBclClauseAheadOfAnotherType_IsReported()
    {
        var warnings = CollectUnsupported(
            """
            try { Work(); }
            catch (InvalidOperationException) { Note(1); }
            catch (FormatException) { Note(2); }
            catch (OverflowException) { Note(3); }
            """
        );

        await Assert
            .That(warnings.Select(ReportedText))
            .IsEquivalentTo(["(InvalidOperationException)", "(FormatException)"]);
    }

    [Test]
    [Arguments(
        """
            try { Work(); }
            catch (InvalidOperationException) { Note(1); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (AppException) { Note(1); }
            catch (InvalidOperationException) { Note(2); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (InvalidOperationException) when (Ready) { Note(1); }
            catch (InvalidOperationException) { Note(2); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (ArgumentException) when (Ready) { Note(1); }
            catch (ArgumentNullException) { Note(2); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (InvalidOperationException) when (Ready) { Note(1); }
            catch (StaleException) { Note(2); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (AppException) { Note(1); }
            catch (Exception) when (Ready) { Note(2); }
            catch { Note(3); }
            """
    )]
    public async Task ClausesTheChainKeepsApart_AreNotReported(string body)
    {
        await Assert.That(CollectUnsupported(body)).IsEmpty();
    }

    [Test]
    public async Task FilterDeclaringAVariable_IsReportedAtTheFilter()
    {
        var (files, diagnostics) = Transpile(
            """
            try { Work(); }
            catch (AppException e) when (e.InnerException is AppException inner) { Note(inner.Code); }
            """
        );

        var warning = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert
            .That(warning.Message)
            .IsEqualTo(
                "Exception filter 'when (e.InnerException is AppException inner)' cannot be "
                    + "lowered: a filter that declares a variable (a pattern designator) or "
                    + "passes an 'out' argument is not supported. Call a method that does it "
                    + "from the filter instead."
            );

        await Assert
            .That(ReportedText(warning))
            .IsEqualTo("when (e.InnerException is AppException inner)");

        await Assert
            .That(files["handler.ts"])
            .Contains("throw new Error(\"Unsupported expression: CatchFilterVariable\");");
    }

    [Test]
    public async Task FilterPassingAnOutArgument_IsReportedAtTheFilter()
    {
        var warning = CollectUnsupported(
                """
                var code = 0;

                try { Work(); }
                catch (AppException e) when (int.TryParse(e.Message, out code)) { Note(code); }
                """
            )
            .Single();

        await Assert
            .That(ReportedText(warning))
            .IsEqualTo("when (int.TryParse(e.Message, out code))");
    }

    [Test]
    public async Task ClauseOnATypeParameter_IsReportedAtTheClause()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Handler
            {
                public void Run<TFailure>() where TFailure : Exception
                {
                    try { Work(); }
                    catch (TFailure) { Work(); }
                }

                public void Work() { }
            }
            """
        );

        var warning = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert
            .That(warning.Message)
            .IsEqualTo(
                "Catch clause '(TFailure)' cannot be lowered: its exception type has no runtime "
                    + "type test in the target (type parameters are erased), so the clause takes "
                    + "every exception."
            );

        await Assert.That(ReportedText(warning)).IsEqualTo("(TFailure)");
    }

    [Test]
    public async Task DartTarget_ReportsEveryFilter_AndNoTypeRule()
    {
        var (_, diagnostics) = TranspileHelper.TranspileDart(
            "using System;\nusing Metano.Annotations;\n"
                + Source(
                    """
                    try { Work(); }
                    catch (InvalidOperationException) { Note(1); }
                    catch (AppException e) when (e.InnerException is AppException inner) { Note(inner.Code); }
                    catch (AppException) when (Ready) { Note(2); }
                    """
                )
        );

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert
            .That(warnings.Select(d => d.Message))
            .IsEquivalentTo([
                "Exception filter 'when (e.InnerException is AppException inner)' cannot be "
                    + "lowered: exception filters are not lowered for Dart; the clause would run "
                    + "unconditionally.",
                "Exception filter 'when (Ready)' cannot be lowered: exception filters are not "
                    + "lowered for Dart; the clause would run unconditionally.",
            ]);

        await Assert
            .That(warnings.Select(ReportedText))
            .IsEquivalentTo(["when (e.InnerException is AppException inner)", "when (Ready)"]);
    }

    private static IReadOnlyList<MetanoDiagnostic> CollectUnsupported(string body) =>
        Transpile(body)
            .Diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string body) => TranspileHelper.TranspileWithDiagnostics(Source(body));

    private static string Source(string body) =>
        $$"""
            [Transpile]
            public class AppException : Exception
            {
                public AppException(string message, int code) : base(message) { Code = code; }

                public int Code { get; }
            }

            [Transpile]
            public class StaleException : InvalidOperationException
            {
                public StaleException(string message) : base(message) { }
            }

            [Transpile]
            public class Handler
            {
                public bool Ready { get; set; }

                public void Run()
                {
                    {{body}}
                }

                public void Work() { }

                public void Note(int value) { }
            }
            """;

    private static string ReportedText(MetanoDiagnostic diagnostic)
    {
        var location = ReportedLocation(diagnostic);

        return location.SourceTree!.GetText().ToString(location.SourceSpan);
    }

    private static string ReportedLineText(MetanoDiagnostic diagnostic)
    {
        var location = ReportedLocation(diagnostic);
        var line = location.GetLineSpan().StartLinePosition.Line;

        return location.SourceTree!.GetText().Lines[line].ToString().Trim();
    }

    /// <summary>The diagnostic's location, which must be in source.</summary>
    private static Microsoft.CodeAnalysis.Location ReportedLocation(MetanoDiagnostic diagnostic)
    {
        if (diagnostic.Location is not { SourceTree: not null } location)
            throw new InvalidOperationException(
                $"'{diagnostic.Message}' was reported without a source location."
            );

        return location;
    }
}
