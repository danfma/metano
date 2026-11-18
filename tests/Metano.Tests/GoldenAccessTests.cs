namespace Metano.Tests;

/// <summary>
/// Guards the single entry point to the golden files: every test compares
/// against <c>Expected/</c> through <see cref="TranspileHelper.AssertGolden"/>,
/// which is the only code that honours <c>METANO_UPDATE_GOLDENS</c>. A test
/// that reads a golden any other way reads the stale copy in the output
/// directory and cannot be regenerated.
/// </summary>
public class GoldenAccessTests
{
    private const string HelperFile = "TranspileHelper.cs";
    private const string GuardFile = "GoldenAccessTests.cs";

    private const string DirectoryReader = "SourceGoldenDirectory(";

    private static readonly string TreeShakingDirectory =
        "TreeShaking" + Path.DirectorySeparatorChar;

    private static readonly string[] ForbiddenFragments =
    [
        "\"Expected\"",
        "Expected/",
        "Expected\\",
        "METANO_UPDATE_GOLDENS",
    ];

    [Test]
    public async Task EveryGoldenReadGoesThroughTheHelper()
    {
        var offenders = TestSourceFiles()
            .Where(file => Path.GetFileName(file) is not (HelperFile or GuardFile))
            .Where(file => ReferencesGoldens(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(TestProjectDirectory(), file))
            .Order()
            .ToList();

        await Assert.That(offenders).IsEmpty();
    }

    /// <summary>
    /// The one sanctioned whole-directory reader, <see cref="TranspileHelper.SourceGoldenDirectory"/>,
    /// serves the golden bundle check (spec 011 FR-043) only: it reads every golden as
    /// a whole and compares none, so it may live under <c>TreeShaking/</c> alone.
    /// </summary>
    [Test]
    public async Task OnlyTheTreeShakingChecksReadTheGoldenDirectory()
    {
        var offenders = TestSourceFiles()
            .Where(file => Path.GetFileName(file) is not (HelperFile or GuardFile))
            .Where(file =>
                File.ReadAllText(file).Contains(DirectoryReader, StringComparison.Ordinal)
            )
            .Select(file => Path.GetRelativePath(TestProjectDirectory(), file))
            .Where(file => !file.StartsWith(TreeShakingDirectory, StringComparison.Ordinal))
            .Order()
            .ToList();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task TheGuardFindsTheHelperSource()
    {
        var helper = TestSourceFiles().Single(file => Path.GetFileName(file) == HelperFile);

        await Assert.That(ReferencesGoldens(File.ReadAllText(helper))).IsTrue();
    }

    private static bool ReferencesGoldens(string source) =>
        ForbiddenFragments.Any(fragment => source.Contains(fragment, StringComparison.Ordinal));

    private static IEnumerable<string> TestSourceFiles() =>
        Directory
            .EnumerateFiles(TestProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string file)
    {
        var relative = Path.GetRelativePath(TestProjectDirectory(), file);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar)[0];

        return firstSegment is "bin" or "obj" or "Expected";
    }

    private static string TestProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (
            current is not null
            && !File.Exists(Path.Combine(current.FullName, "Metano.Tests.csproj"))
        )
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException(
                "Metano.Tests.csproj not found above the test output directory"
            );
    }
}
