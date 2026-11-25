using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Packaging;

namespace Metano.Tests;

/// <summary>
/// <c>package.json#sideEffects</c> lists the modules that run code when merely imported
/// (<c>[ModuleEntryPoint]</c> bodies) instead of a flat
/// <c>false</c> that lets bundlers drop them (#356, #368).
/// </summary>
public class SideEffectsManifestTests
{
    private const string EntryPointSource = """
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Acme;

        [NoContainer]
        public static class Program
        {
            public static int Helper(int x) => x * 2;

            [ModuleEntryPoint]
            public static void Main() => System.Console.WriteLine("hi");
        }

        public record Money(int Amount);
        """;

    private const string StrictUnionSource = """
        using Metano.Annotations;
        using Metano.Annotations.TypeScript;

        [assembly: TranspileAssembly]

        namespace Acme;

        [StringEnum]
        public enum ShapeKind { Circle, Square }

        [GenerateGuard, StrictUnionGuard]
        [Discriminator("Kind")]
        public abstract record Shape(ShapeKind Kind);

        [GenerateGuard]
        [Discriminator("Kind")]
        public sealed record Circle(ShapeKind Kind, double Radius) : Shape(Kind);
        """;

    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task EntryPointModule_IsTheOnlyListedModule()
    {
        var sideEffects = SideEffectsOf(WriteFromSource(EntryPointSource));

        await Assert
            .That(sideEffects)
            .IsEquivalentTo(["./dist/acme/program.js", "./src/acme/program.ts"]);
    }

    /// <summary>
    /// A strict union registers nothing at load since spec 011 FR-029 (its
    /// guard module imports the variants), so it lists no module.
    /// </summary>
    [Test]
    public async Task StrictUnionVariant_IsNotListed()
    {
        var root = WriteFromSource(StrictUnionSource);

        await Assert.That(ReadJson(root)["sideEffects"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task NoSideEffectModule_WritesFlatFalse()
    {
        var root = WriteFromSource(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Acme;

            public record Money(int Amount);
            """
        );

        await Assert.That(ReadJson(root)["sideEffects"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task FlatFalseWrittenEarlier_IsUpgradedToTheList()
    {
        var root = CreateTempDir();

        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """{ "name": "consumer", "sideEffects": false }"""
        );

        Write(root, EntryPointFiles());

        await Assert
            .That(SideEffectsOf(root))
            .IsEquivalentTo(["./dist/program.js", "./src/program.ts"]);
    }

    [Test]
    public async Task HandWrittenEntries_SurviveAndStaleOwnedOnesAreReplaced()
    {
        var root = CreateTempDir();

        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """{ "name": "consumer", "sideEffects": ["./styles.css", "./dist/gone.js"] }"""
        );

        Write(root, EntryPointFiles());

        await Assert
            .That(SideEffectsOf(root))
            .IsEquivalentTo(["./styles.css", "./dist/program.js", "./src/program.ts"]);
    }

    /// <summary>
    /// A package whose own <c>#/*</c> import points at another compiled tree
    /// (<c>./lib</c>, a Vite app's <c>outDir</c>) lists the side-effect module there; the
    /// entry an earlier run wrote under the default <c>./dist</c> is replaced.
    /// </summary>
    [Test]
    public async Task RetargetedCompiledTree_ListsTheModuleUnderIt()
    {
        var root = CreateTempDir();

        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """
            {
              "name": "consumer",
              "sideEffects": ["./dist/program.js", "./src/program.ts"],
              "imports": { "#/*": { "types": "./lib/*", "import": "./lib/*", "default": "./src/*" } }
            }
            """
        );

        Write(root, EntryPointFiles());

        await Assert
            .That(SideEffectsOf(root))
            .IsEquivalentTo(["./lib/program.js", "./src/program.ts"]);
    }

    [Test]
    public async Task HandWrittenEntryThatIsNotAString_IsKept()
    {
        var root = CreateTempDir();

        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """{ "name": "consumer", "sideEffects": ["./styles.css", 42] }"""
        );

        Write(root, EntryPointFiles());

        var entries = ((JsonArray)ReadJson(root)["sideEffects"]!)
            .Select(entry => entry!.ToJsonString())
            .ToList();

        await Assert
            .That(entries)
            .IsEquivalentTo([
                "\"./styles.css\"",
                "42",
                "\"./dist/program.js\"",
                "\"./src/program.ts\"",
            ]);
    }

    [Test]
    public async Task ListShrinksBackToFalse_WhenTheEntryPointDisappears()
    {
        var root = CreateTempDir();

        Write(root, EntryPointFiles());
        Write(root, [new TsSourceFile("money.ts", [])]);

        await Assert.That(ReadJson(root)["sideEffects"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task HandWrittenTrue_IsLeftAlone()
    {
        var root = CreateTempDir();

        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """{ "name": "consumer", "sideEffects": true }"""
        );

        Write(root, EntryPointFiles());

        await Assert.That(ReadJson(root)["sideEffects"]!.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task FullCacheHit_RestoresTheSideEffectModules()
    {
        var compilation = TranspileHelper.CompileLibrary(EntryPointSource);
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        var fresh = new TypeScriptTarget();
        var output = fresh.Transform(ir, compilation);
        var adopted = new TypeScriptTarget();

        adopted.AdoptCachedOutput(
            ir,
            compilation,
            new CachedTargetOutput(
                output.Files,
                output.PackageDependencies,
                output.ModuleSideEffectFiles,
                []
            )
        );

        var freshRoot = CreateTempDir();
        var adoptedRoot = CreateTempDir();

        Write(freshRoot, fresh.LastPackageManifestInputs.SourceFiles);
        Write(adoptedRoot, adopted.LastPackageManifestInputs.SourceFiles);

        await Assert.That(SideEffectsOf(adoptedRoot)).IsEquivalentTo(SideEffectsOf(freshRoot));
        await Assert.That(SideEffectsOf(adoptedRoot)).IsNotEmpty();
    }

    private static IReadOnlyList<TsSourceFile> EntryPointFiles() =>
        [
            new TsSourceFile("program.ts", []) { HasModuleSideEffects = true },
            new TsSourceFile("money.ts", []),
        ];

    private string WriteFromSource(string source)
    {
        var compilation = TranspileHelper.CompileLibrary(source);
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        var target = new TypeScriptTarget();
        var root = CreateTempDir();

        target.Transform(ir, compilation);
        Write(root, target.LastPackageManifestInputs.SourceFiles);

        return root;
    }

    private static void Write(string root, IReadOnlyList<TsSourceFile> files) =>
        PackageJsonWriter.UpdateOrCreate(
            root,
            Path.Combine(root, "src"),
            files,
            authoritativePackageName: "consumer",
            srcRoot: "src"
        );

    private static string[] SideEffectsOf(string root) =>
        [.. ((JsonArray)ReadJson(root)["sideEffects"]!).Select(entry => entry!.GetValue<string>())];

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-side-effects-{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }
}
