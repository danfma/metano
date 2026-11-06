using System.Diagnostics;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The transpiled 64-bit scenario of <c>samples/SampleIssueTracker</c> (spec
/// 012 FR-068): the sample transpiled in the staging
/// <see cref="Int64Representation.BigInt"/> mode, which has no MSBuild
/// surface (DECIDED-22), must equal the committed
/// <c>targets/js/sample-issue-tracker/src</c>, whose bun tests round-trip
/// the snowflake ids and the tick math through its <c>JsonContext</c>.
///
/// <para>The committed output keeps the Biome formatting the build target
/// used to apply, so the test writes the transpiled files, runs the same
/// <c>biome check --write</c> pass over them, and then compares. A failing
/// run restores the committed bytes first, so a red test leaves the tree
/// clean. Setting <c>METANO_UPDATE_INT64_ISSUE_TRACKER=1</c> keeps the
/// regenerated output instead (local regeneration only; CI never sets it).</para>
/// </summary>
public class Int64IssueTrackerSampleTests
{
    private const string SolutionFile = "Metano.slnx";

    private const string UpdateEnvironmentVariable = "METANO_UPDATE_INT64_ISSUE_TRACKER";

    private const string FilePrefix = """
        /** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
        /** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
        """;

    private static readonly bool UpdateOutput =
        Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) == "1";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string SampleDirectory = Path.Combine(
        RepositoryRoot,
        "samples",
        "SampleIssueTracker"
    );

    private static readonly string OutputDirectory = Path.Combine(
        RepositoryRoot,
        "targets",
        "js",
        "sample-issue-tracker",
        "src"
    );

    [Test]
    public async Task BigIntMode_TranspiledSample_MatchesTheCommittedOutput()
    {
        var (files, diagnostics) = TranspileHelper.TranspileProjectDirectory(
            SampleDirectory,
            Int64Representation.BigInt
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsEmpty();

        var generated = files.ToDictionary(file => file.Key, file => $"{FilePrefix}\n{file.Value}");

        var committed = CommittedFiles();

        WriteAll(generated);
        NormalizeWithBiome();
        var regenerated = CommittedFiles();

        if (UpdateOutput)
        {
            DeleteExtraneous(generated);

            return;
        }

        Restore(committed);

        await Assert.That(regenerated.Keys.Order()).IsEquivalentTo(committed.Keys.Order());

        var drift = regenerated
            .Where(file => !committed.TryGetValue(file.Key, out var text) || text != file.Value)
            .Select(file => file.Key)
            .Order()
            .ToList();

        await Assert.That(drift).IsEmpty();
    }

    /// <summary>
    /// The BigInt mode has no MSBuild surface, so the sample must keep no
    /// transpile wiring in its project file — the drift test above is the
    /// generator (the twin of the conformance sample's own guard).
    /// </summary>
    [Test]
    public async Task SampleProject_KeepsNoMsBuildTranspileWiring()
    {
        var project = File.ReadAllText(Path.Combine(SampleDirectory, "SampleIssueTracker.csproj"));

        await Assert.That(project).DoesNotContain("MetanoOutputDir");
    }

    private static Dictionary<string, string> CommittedFiles() =>
        Directory
            .GetFiles(OutputDirectory, "*.ts", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(OutputDirectory, path).Replace('\\', '/'),
                File.ReadAllText
            );

    private static void WriteAll(Dictionary<string, string> files)
    {
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(OutputDirectory, name);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private static void Restore(Dictionary<string, string> committed)
    {
        foreach (
            var path in Directory.GetFiles(OutputDirectory, "*.ts", SearchOption.AllDirectories)
        )
        {
            var name = Path.GetRelativePath(OutputDirectory, path).Replace('\\', '/');

            if (!committed.TryGetValue(name, out var text))
            {
                File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, text);
            }
        }
    }

    /// <summary>The committed files a type removal left behind, dropped on regeneration.</summary>
    private static void DeleteExtraneous(Dictionary<string, string> generated)
    {
        foreach (
            var path in Directory.GetFiles(OutputDirectory, "*.ts", SearchOption.AllDirectories)
        )
        {
            var name = Path.GetRelativePath(OutputDirectory, path).Replace('\\', '/');

            if (!generated.ContainsKey(name))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// The Biome pass the build target ran over the transpiled output, which
    /// normalizes line wrapping, trailing commas and import types.
    /// </summary>
    private static void NormalizeWithBiome()
    {
        var relativeOutput = Path.GetRelativePath(RepositoryRoot, OutputDirectory);
        var startInfo = new ProcessStartInfo("bunx", $"biome check --write {relativeOutput}")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(startInfo)!;

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var output =
                $"{process.StandardOutput.ReadToEnd()}\n{process.StandardError.ReadToEnd()}";

            throw new InvalidOperationException(
                $"biome check --write {relativeOutput} failed:\n{output}"
            );
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
