using Metano.Compiler.Watch;

namespace Metano.Tests.Watch;

/// <summary>
/// Coverage for the pure pieces of <see cref="WatchHost"/>. The
/// orchestrator itself is exercised manually — <c>FileSystemWatcher</c>
/// semantics vary by platform and are too flaky for a reliable unit
/// test (ADR-0022).
/// </summary>
public class WatchHostTests
{
    [Test]
    [Arguments("Foo.cs", true)]
    [Arguments("Foo.CS", true)]
    [Arguments("Project.csproj", true)]
    [Arguments("Directory.Build.props", true)]
    [Arguments("Metano.Build.targets", true)]
    [Arguments("README.md", false)]
    [Arguments("foo.tsbuildinfo", false)]
    [Arguments("noextension", false)]
    [Arguments("", false)]
    public async Task IsRelevant_AcceptsBuildInputs_RejectsEverythingElse(
        string fileName,
        bool expected
    )
    {
        var path = string.IsNullOrEmpty(fileName) ? string.Empty : Path.Combine("/proj", fileName);
        await Assert.That(WatchHost.IsRelevant(path)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/proj/bin/Debug/Foo.cs")]
    [Arguments("/proj/obj/Debug/Foo.cs")]
    [Arguments("/proj/sub/bin/Foo.csproj")]
    [Arguments("/proj/BIN/Debug/Foo.cs")]
    [Arguments("/proj/OBJ/Debug/Foo.cs")]
    public async Task IsRelevant_ExcludesBuildArtifactDirectories(string path)
    {
        await Assert.That(WatchHost.IsRelevant(path)).IsFalse();
    }

    [Test]
    public async Task IsRelevant_AllowsFilesUnderRegularSubdirectories()
    {
        await Assert.That(WatchHost.IsRelevant("/proj/Models/User.cs")).IsTrue();
        await Assert.That(WatchHost.IsRelevant("/proj/src/feature/Service.cs")).IsTrue();
    }
}
