using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The diagnostics of the catch clauses the single-<c>catch</c> lowering
/// cannot keep faithful (#628, #651): MS0045 for a clause on an exception type
/// with no runtime class (a BCL exception outside the runtime mapping, spec
/// 011 FR-014), MS0001 for a clause on a type parameter (no runtime test) and
/// for a filter that declares a variable or passes an <c>out</c> argument.
/// Clauses on mapped BCL exceptions test their runtime class, so the chain
/// keeps them apart in any order.
/// </summary>
public class CatchClauseDiagnosticTests
{
    [Test]
    public async Task ClauseOnAnUnmappedBclException_IsAnErrorAtTheClause()
    {
        var (_, diagnostics) = Transpile(
            """
            try { Work(); }
            catch (TimeoutException) { Note(1); }
            catch (InvalidOperationException) { Note(2); }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnanswerableRuntimeTypeTest);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(ReportedText(error)).IsEqualTo("(TimeoutException)");

        await Assert
            .That(error.Message)
            .IsEqualTo(
                "'(TimeoutException)' cannot be lowered: 'System.TimeoutException' has no runtime "
                    + "class in the target, so no type test can tell it apart from other "
                    + "exceptions, and testing for 'Exception' instead would take them all (spec "
                    + "011 FR-014). Catch or test a base type the runtime defines, or a transpiled "
                    + "exception of your own."
            );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task LoneClauseOnAnUnmappedBclException_IsReported()
    {
        var (_, diagnostics) = Transpile(
            """
            try { Work(); }
            catch (System.IO.IOException e) { Note(e.Message.Length); }
            """
        );

        await Assert
            .That(
                diagnostics
                    .Where(d => d.Code == DiagnosticCodes.UnanswerableRuntimeTypeTest)
                    .Select(ReportedText)
            )
            .IsEquivalentTo(["(System.IO.IOException e)"]);
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
    [Arguments(
        """
            try { Work(); }
            catch (InvalidOperationException) { Note(1); }
            catch (OverflowException) { Note(2); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (FormatException e) { Note(e.Message.Length); }
            catch (Exception) { Note(2); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (InvalidOperationException) when (Ready) { Note(1); }
            catch (AppException) { Note(2); }
            """
    )]
    [Arguments(
        """
            try { Work(); }
            catch (InvalidOperationException) { Note(1); }
            catch (FormatException) { Note(2); }
            catch (OverflowException) { Note(3); }
            """
    )]
    public async Task ClausesTheChainKeepsApart_AreNotReported(string body)
    {
        var diagnostics = Transpile(body).Diagnostics;

        await Assert
            .That(
                diagnostics.Where(d =>
                    d.Code
                        is DiagnosticCodes.UnsupportedFeature
                            or DiagnosticCodes.UnanswerableRuntimeTypeTest
                )
            )
            .IsEmpty();
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
                    catch (TimeoutException) { Note(3); }
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

        // Dart tests every exception type natively: no clause needs a runtime class.
        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnanswerableRuntimeTypeTest))
            .IsEmpty();
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
