using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Two project types printed under one name that one file refers to would share a single
/// import and silently bind the wrong class, so MS0070 reports the second reference (#504).
/// </summary>
public class SameNameTypeClashTests
{
    private const string TwoR23 = """
        namespace Na
        {
            [Transpile]
            public class R23
            {
                public string Tag => "a";
            }
        }

        namespace Nb
        {
            [Transpile]
            public class R23
            {
                public string Tag => "b";
            }
        }

        """;

    [Test]
    public async Task TwoTypesOfOneNameInOneFile_ReportTheSecondReference()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            TwoR23
                + """
                [Transpile]
                public class Probe
                {
                    public string Run() => new Na.R23().Tag + new Nb.R23().Tag;
                }
                """
        );

        var clash = diagnostics.Single(d => d.Code == DiagnosticCodes.ImportedNameClash);

        await Assert.That(clash.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(clash.Message).Contains("'R23' names both 'Na.R23' and 'Nb.R23'");

        await Assert
            .That(
                clash
                    .Location!.SourceTree!.GetText()
                    .Lines[clash.Location.GetLineSpan().StartLinePosition.Line]
                    .ToString()
            )
            .Contains("new Nb.R23()");
    }

    [Test]
    public async Task OneOfTheTwoTypes_ReferencedAlone_ReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            TwoR23
                + """
                [Transpile]
                public class Probe
                {
                    public string Run() => new Na.R23().Tag;
                }

                [Transpile]
                public class Other
                {
                    public string Run() => new Nb.R23().Tag;
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();
    }

    [Test]
    public async Task OneRenamedWithName_ReportsNothing()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Na
            {
                [Transpile]
                public class R23 { }
            }

            namespace Nb
            {
                [Transpile, Name("R23b")]
                public class R23 { }
            }

            [Transpile]
            public class Probe
            {
                public object Run() => new object[] { new Na.R23(), new Nb.R23() };
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        await Assert.That(files["probe.ts"]).Contains("new R23b()");
    }
}
