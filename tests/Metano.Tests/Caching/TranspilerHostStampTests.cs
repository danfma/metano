using Metano.Compiler;

namespace Metano.Tests.Caching;

/// <summary>
/// #334: a post-emit stage that fails after a successful transpile removes the MSBuild
/// stamp, so the next incremental build re-runs the target and reports the error again.
/// </summary>
public class TranspilerHostStampTests
{
    private readonly string _outputDir = Directory.CreateTempSubdirectory("metano-stamp-").FullName;

    private string StampPath => Path.Combine(_outputDir, ".metano-stamp");

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [Test]
    public async Task DeleteStamp_RemovesTheStamp()
    {
        File.WriteAllText(StampPath, "");

        TranspilerHost.DeleteStamp(_outputDir);

        await Assert.That(File.Exists(StampPath)).IsFalse();
    }

    [Test]
    public async Task DeleteStamp_WithoutAStamp_DoesNothing()
    {
        TranspilerHost.DeleteStamp(Path.Combine(_outputDir, "missing"));

        await Assert.That(File.Exists(StampPath)).IsFalse();
    }
}
