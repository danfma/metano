using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.TypeScript;
using Metano.Packaging;

namespace Metano.Tests;

/// <summary>
/// A generated package that declares a TypeScript <c>using</c> lists <c>tslib</c> as an
/// optional peer dependency, the home of the downlevelled disposal helpers under
/// <c>importHelpers</c> (FR-111 A1.4, decision W11-1a).
/// </summary>
public class DisposalHelpersPeerDependencyTests
{
    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    [Test]
    [Arguments("    using r = new Res();", true)]
    [Arguments("  using $using0: Res | null = open();", true)]
    [Arguments("import { x } from \"using\";", false)]
    [Arguments("    const using = 1;", false)]
    [Arguments("    // using r = later", false)]
    public async Task IsNeededBy_DetectsUsingDeclarations(string line, bool expected)
    {
        var files = new[] { new GeneratedFile("a.ts", $"{line}\n") };

        await Assert.That(DisposalHelpersPeerDependency.IsNeededBy(files)).IsEqualTo(expected);
    }

    [Test]
    public async Task Writer_DeclaresTslibAsAnOptionalPeer()
    {
        var root = CreateTempDir();

        Write(root);

        var pkg = ReadJson(root);

        await Assert
            .That(pkg["peerDependencies"]?["tslib"]?.GetValue<string>())
            .IsEqualTo("^2.6.0");

        await Assert
            .That(pkg["peerDependenciesMeta"]?["tslib"]?["optional"]?.GetValue<bool>())
            .IsTrue();
    }

    [Test]
    public async Task Writer_KeepsTheConsumersOwnTslibEntry()
    {
        var root = CreateTempDir();

        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """{ "name": "consumer", "devDependencies": { "tslib": "2.8.1" } }"""
        );

        Write(root);

        var pkg = ReadJson(root);

        await Assert.That(pkg["devDependencies"]?["tslib"]?.GetValue<string>()).IsEqualTo("2.8.1");
        await Assert.That(pkg.ContainsKey("peerDependencies")).IsFalse();
    }

    [Test]
    public async Task Writer_RemovesItsOwnTslibPeerOnceNoUsingIsDeclared()
    {
        var root = CreateTempDir();

        Write(root);
        Write(root, declaresDisposalHelpers: false);

        var pkg = ReadJson(root);

        await Assert.That(pkg.ContainsKey("peerDependencies")).IsFalse();
        await Assert.That(pkg.ContainsKey("peerDependenciesMeta")).IsFalse();
    }

    [Test]
    public async Task Writer_KeepsAConsumersTslibPeerWhenNoUsingIsDeclared()
    {
        var root = CreateTempDir();

        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """{ "name": "consumer", "peerDependencies": { "tslib": "^2.8.0" } }"""
        );

        Write(root, declaresDisposalHelpers: false);

        await Assert
            .That(ReadJson(root)["peerDependencies"]?["tslib"]?.GetValue<string>())
            .IsEqualTo("^2.8.0");
    }

    [Test]
    public async Task Transform_UsingDeclaration_NeedsTheHelpersOnlyWhenNative()
    {
        const string Source = """
            using System;
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Acme;

            public sealed class Res : IDisposable { public void Dispose() { } }

            public static class Uses
            {
                public static void Run() { using var r = new Res(); }
            }
            """;

        await Assert.That(InputsOf(Source, UsingLowering.Native).NeedsDisposalHelpers).IsTrue();

        await Assert
            .That(InputsOf(Source, UsingLowering.TryFinally).NeedsDisposalHelpers)
            .IsFalse();
    }

    private static PackageManifestInputs InputsOf(string source, UsingLowering lowering)
    {
        var compilation = TranspileHelper.CompileLibrary(source);
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        var target = new TypeScriptTarget { UsingLowering = lowering };

        target.Transform(ir, compilation);

        return target.LastPackageManifestInputs;
    }

    private static void Write(string root, bool declaresDisposalHelpers = true)
    {
        var srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(srcDir);

        PackageJsonWriter.UpdateOrCreate(
            root,
            srcDir,
            [],
            authoritativePackageName: "consumer",
            declaresDisposalHelpers: declaresDisposalHelpers
        );
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-tslib-peer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }

    private static JsonObject ReadJson(string dir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json")))!.AsObject();
}
