using Metano.Compiler;
using Metano.Compiler.Caching;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests.Caching;

/// <summary>
/// Issue #785: a Metano warning is reported by every build, not only the one that produced
/// it, the project's warning settings escalate it, and a run that failed leaves no up-to-date
/// stamp. The source has one real warning (MS0001, an exception base without a runtime class)
/// reported while its group transforms, so the per-group cache is exercised too.
/// </summary>
public class DiagnosticReplayTests
{
    private const string OtherFile = "Other.cs";

    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-diagnostics-")
        .FullName;

    private string OutputDir => Path.Combine(_root, "src");

    private string StampPath => Path.Combine(OutputDir, ".metano-stamp");

    private string CachePath => Path.Combine(OutputDir, TranspilationCache.FileName);

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task Warning_IsReportedAgainByARunServedFromTheCache()
    {
        var first = await Run();
        var second = await Run();

        await Assert.That(first.Result.WarningCount).IsEqualTo(1);
        await Assert.That(second.ServedFromCache).IsTrue();
        await Assert.That(second.Result.WarningCount).IsEqualTo(1);
        await Assert.That(second.Result.Success).IsTrue();
    }

    [Test]
    public async Task Warning_IsReportedAgainWhenOnlyAnotherGroupChanged()
    {
        await Run();

        var afterEdit = await Run(otherSource: "public record Other(int X, int Y);");
        var afterEditAgain = await Run(otherSource: "public record Other(int X, int Y);");

        await Assert.That(afterEdit.ServedFromCache).IsFalse();
        await Assert.That(afterEdit.Result.WarningCount).IsEqualTo(1);
        await Assert.That(afterEditAgain.ServedFromCache).IsTrue();
        await Assert.That(afterEditAgain.Result.WarningCount).IsEqualTo(1);
    }

    [Test]
    public async Task EscalatedWarning_FailsTheRunAndWritesNeitherStampNorCache()
    {
        var run = await Run(new DiagnosticPolicy(TreatWarningsAsErrors: true));

        await Assert.That(run.Result.Success).IsFalse();
        await Assert.That(run.Result.ErrorCount).IsEqualTo(1);
        await Assert.That(File.Exists(StampPath)).IsFalse();
        await Assert.That(File.Exists(CachePath)).IsFalse();
    }

    [Test]
    public async Task EscalatedWarning_FailsAgainOnTheNextRun()
    {
        var policy = new DiagnosticPolicy(TreatWarningsAsErrors: true);

        var first = await Run(policy);
        var second = await Run(policy);

        await Assert.That(first.Result.Success).IsFalse();
        await Assert.That(second.Result.Success).IsFalse();
        await Assert.That(File.Exists(StampPath)).IsFalse();
    }

    [Test]
    public async Task EscalatedWarning_FailsARunServedFromTheCacheAndRemovesTheStamp()
    {
        await Run();
        await Assert.That(File.Exists(StampPath)).IsTrue();

        var escalated = await Run(
            new DiagnosticPolicy(
                WarningsAsErrors: new HashSet<string> { DiagnosticCodes.UnsupportedFeature }
            )
        );

        await Assert.That(escalated.ServedFromCache).IsTrue();
        await Assert.That(escalated.Result.Success).IsFalse();
        await Assert.That(escalated.Result.ErrorCount).IsEqualTo(1);
        await Assert.That(File.Exists(StampPath)).IsFalse();
    }

    [Test]
    public async Task WarningsNotAsErrors_KeepsTheWarningAWarning()
    {
        var policy = new DiagnosticPolicy(
            TreatWarningsAsErrors: true,
            WarningsNotAsErrors: new HashSet<string> { DiagnosticCodes.UnsupportedFeature }
        );

        var run = await Run(policy);

        await Assert.That(run.Result.Success).IsTrue();
        await Assert.That(run.Result.WarningCount).IsEqualTo(1);
        await Assert.That(File.Exists(StampPath)).IsTrue();
    }

    [Test]
    public async Task NoWarn_DropsTheWarningEvenWhenEscalated()
    {
        var policy = new DiagnosticPolicy(
            TreatWarningsAsErrors: true,
            NoWarn: new HashSet<string> { DiagnosticCodes.UnsupportedFeature }
        );

        var run = await Run(policy);

        await Assert.That(run.Result.Success).IsTrue();
        await Assert.That(run.Result.WarningCount).IsEqualTo(0);
    }

    [Test]
    public async Task AppendReplayLines_KeepsTheLaterStageWarningsBesideTheRunsOwn()
    {
        await Run();

        TranspilerHost.AppendReplayLines(
            OutputDir,
            [
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    "MS0007",
                    "package name differs"
                ),
                new MetanoDiagnostic(MetanoDiagnosticSeverity.Info, "MS0099", "ignored"),
            ]
        );

        var lines = File.ReadAllLines(Path.Combine(OutputDir, ".metano-diagnostics"));

        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[1]).IsEqualTo("warning MS0007: package name differs");
    }

    private sealed record HostRun(TranspileResult Result, bool ServedFromCache);

    private async Task<HostRun> Run(
        DiagnosticPolicy? policy = null,
        string otherSource = "public record Other(int X);"
    )
    {
        var recordingTarget = new CacheAdoptionRecorder(new TypeScriptTarget());

        var options = new TranspileOptions(
            Path.Combine(_root, "Notes.csproj"),
            OutputDir,
            DiagnosticPolicy: policy
        );

        var result = await TranspilerHost.RunAsync(
            options,
            recordingTarget,
            new InMemorySourceFrontend(Compile(otherSource))
        );

        return new HostRun(result, recordingTarget.AdoptedCachedOutput);
    }

    private CSharpCompilation Compile(string otherSource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);

        return CSharpCompilation.Create(
            "Notes",
            [
                CSharpSyntaxTree.ParseText(
                    """
                    using Metano.Annotations;

                    [assembly: TranspileAssembly]

                    namespace Acme.Notes
                    {
                        public class DiskFailure : System.IO.IOException;
                    }
                    """,
                    parseOptions,
                    path: Path.Combine(_root, "Casts.cs")
                ),
                CSharpSyntaxTree.ParseText(
                    $$"""
                    using Metano.Annotations;

                    namespace Acme.Notes.Other
                    {
                        {{otherSource}}
                    }
                    """,
                    parseOptions,
                    path: Path.Combine(_root, OtherFile)
                ),
            ],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
