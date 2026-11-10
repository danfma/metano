using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The transpiled twin of the spec 012 reference matrix (FR-064):
/// <c>samples/SampleInt64Conformance</c> and its sibling
/// <c>samples/SampleCheckedConformance</c> (compiled with
/// <c>CheckForOverflowUnderflow</c>), which this test transpiles (the samples
/// have no MSBuild transpile step), must equal the committed
/// <c>targets/js/sample-int64-conformance/src</c>, whose bun and Node tests
/// run it against the fixtures of <c>tests/Int64Reference</c>.
///
/// <para>Setting <c>METANO_UPDATE_INT64_CONFORMANCE=1</c> rewrites the
/// committed output (local regeneration only; CI never sets it).</para>
/// </summary>
public class Int64ConformanceSampleTests
{
    private const string SolutionFile = "Metano.slnx";
    private const string DefaultContextSample = "SampleInt64Conformance";
    private const string DefaultContextFolder = "sample-int64-conformance";
    private const string CheckedContextSample = "SampleCheckedConformance";
    private const string CheckedContextFolder = "sample-checked-conformance";

    private static readonly bool UpdateOutput =
        Environment.GetEnvironmentVariable("METANO_UPDATE_INT64_CONFORMANCE") == "1";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string OutputDirectory = Path.Combine(
        RepositoryRoot,
        "targets",
        "js",
        "sample-int64-conformance",
        "src"
    );

    [Test]
    public async Task TranspiledTwin_MatchesTheCommittedOutput()
    {
        var (files, diagnostics) = TranspileHelper.TranspileSourceDirectory(
            SampleDirectory(DefaultContextSample)
        );

        await AssertMatchesTheCommittedOutput(DefaultContextFolder, files, diagnostics);
    }

    /// <summary>
    /// The sibling twin is compiled with the overflow checks its project
    /// sets, so its output checks every operation outside an
    /// <c>unchecked</c> context (spec 012 DECIDED-4).
    /// </summary>
    [Test]
    public async Task CheckedTwin_MatchesTheCommittedOutput()
    {
        var (files, diagnostics) = TranspileHelper.TranspileSourceDirectory(
            SampleDirectory(CheckedContextSample),
            checkOverflow: true
        );

        await AssertMatchesTheCommittedOutput(CheckedContextFolder, files, diagnostics);
    }

    /// <summary>
    /// The drift test compiles the twin in memory with the default options, and
    /// tests/Int64Reference builds it through its csproj: an overflow-checking
    /// project option would make the fixtures throw where the TypeScript wraps.
    /// </summary>
    [Test]
    public async Task TwinProject_KeepsTheDefaultOverflowContext()
    {
        var project = File.ReadAllText(ProjectFile(DefaultContextSample));

        await Assert.That(project).DoesNotContain("CheckForOverflowUnderflow");
    }

    /// <summary>
    /// The sibling's drift test compiles it with overflow checks, so its
    /// csproj, which tests/Int64Reference builds, must set the same option.
    /// </summary>
    [Test]
    public async Task CheckedTwinProject_ChecksOverflow()
    {
        var project = File.ReadAllText(ProjectFile(CheckedContextSample));

        await Assert
            .That(project)
            .Contains("<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>");
    }

    private static async Task AssertMatchesTheCommittedOutput(
        string folder,
        Dictionary<string, string> files,
        IReadOnlyList<MetanoDiagnostic> diagnostics
    )
    {
        if (UpdateOutput)
            Rewrite(folder, files);

        var committed = CommittedFiles(folder);

        await Assert.That(committed.Keys.Order()).IsEquivalentTo(files.Keys.Order());

        foreach (var (name, content) in files)
            await Assert.That(committed[name]).IsEqualTo(content);

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();
    }

    private static string SampleDirectory(string sample) =>
        Path.Combine(RepositoryRoot, "samples", sample);

    private static string ProjectFile(string sample) =>
        Path.Combine(SampleDirectory(sample), sample + ".csproj");

    /// <summary>The committed files of one twin, keyed as the transpiler names them (<c>folder/file.ts</c>).</summary>
    private static Dictionary<string, string> CommittedFiles(string folder)
    {
        var twinDirectory = Path.Combine(OutputDirectory, folder);

        if (!Directory.Exists(twinDirectory))
            return [];

        return Directory
            .GetFiles(twinDirectory, "*.ts", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(OutputDirectory, path).Replace('\\', '/'),
                File.ReadAllText
            );
    }

    private static void Rewrite(string folder, Dictionary<string, string> files)
    {
        var twinDirectory = Path.Combine(OutputDirectory, folder);

        if (Directory.Exists(twinDirectory))
            Directory.Delete(twinDirectory, recursive: true);

        foreach (var (name, content) in files)
        {
            var path = Path.Combine(OutputDirectory, name);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile))
        )
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException($"No {SolutionFile} above the test binaries.");
    }
}
