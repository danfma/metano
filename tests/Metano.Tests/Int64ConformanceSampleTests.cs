using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The transpiled twin of the spec 012 reference matrix (FR-064):
/// <c>samples/SampleInt64Conformance</c> transpiled in the staging
/// <see cref="Int64Representation.BigInt"/> mode, which has no MSBuild
/// surface (DECIDED-22), must equal the committed
/// <c>targets/js/sample-int64-conformance/src</c>, whose bun and Node tests
/// run it against the fixtures of <c>tests/Int64Reference</c>.
///
/// <para>Setting <c>METANO_UPDATE_INT64_CONFORMANCE=1</c> rewrites the
/// committed output (local regeneration only; CI never sets it).</para>
/// </summary>
public class Int64ConformanceSampleTests
{
    private const string SolutionFile = "Metano.slnx";

    private static readonly bool UpdateOutput =
        Environment.GetEnvironmentVariable("METANO_UPDATE_INT64_CONFORMANCE") == "1";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string SampleDirectory = Path.Combine(
        RepositoryRoot,
        "samples",
        "SampleInt64Conformance"
    );

    private static readonly string OutputDirectory = Path.Combine(
        RepositoryRoot,
        "targets",
        "js",
        "sample-int64-conformance",
        "src"
    );

    [Test]
    public async Task BigIntMode_TranspiledTwin_MatchesTheCommittedOutput()
    {
        var (files, diagnostics) = TranspileHelper.TranspileSourceDirectory(
            SampleDirectory,
            Int64Representation.BigInt
        );

        if (UpdateOutput)
            Rewrite(files);

        var committed = CommittedFiles();

        await Assert.That(committed.Keys.Order()).IsEquivalentTo(files.Keys.Order());

        foreach (var (name, content) in files)
            await Assert.That(committed[name]).IsEqualTo(content);

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();
    }

    /// <summary>
    /// The drift test compiles the twin in memory with the default options, and
    /// tests/Int64Reference builds it through its csproj: an overflow-checking
    /// project option would make the fixtures throw where the TypeScript wraps.
    /// </summary>
    [Test]
    public async Task TwinProject_KeepsTheDefaultOverflowContext()
    {
        var project = File.ReadAllText(
            Path.Combine(SampleDirectory, "SampleInt64Conformance.csproj")
        );

        await Assert.That(project).DoesNotContain("CheckForOverflowUnderflow");
    }

    private static Dictionary<string, string> CommittedFiles() =>
        Directory
            .GetFiles(OutputDirectory, "*.ts", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(OutputDirectory, path).Replace('\\', '/'),
                File.ReadAllText
            );

    private static void Rewrite(Dictionary<string, string> files)
    {
        if (Directory.Exists(OutputDirectory))
            Directory.Delete(OutputDirectory, recursive: true);

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
