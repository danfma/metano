using System.Diagnostics;

namespace Metano.Tests.TreeShaking;

/// <summary>
/// The pure-object companion of a <c>[Branded]</c> / <c>[InlineWrapper]</c> type
/// (spec 011 FR-040) type-checks under <c>tsgo --strict</c>, declared in a library
/// package and used from a consumer package: the value <c>Money.from(…)</c>, the
/// overload dispatcher, the type position and an implicit conversion. The declarations
/// of the library honour <c>stripInternal</c>: an <c>internal</c> member is dropped from
/// the companion's declared type.
/// </summary>
public class CompanionStrictTypeCheckTests
{
    private const string Library = """
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("acme-lib")]

        namespace Acme;

        [InlineWrapper]
        public readonly record struct Money(int Cents)
        {
            public static Money FromCents(int cents) => new(cents);

            public static Money From(int cents) => new(cents);

            public static Money From(string text) => new(text.Length);

            public static implicit operator int(Money money) => money.Cents;
        }
        """;

    private const string TaggedLibrary = """
        [assembly: TranspileAssembly]

        namespace Acme;

        [InlineWrapper]
        public readonly record struct Tag(string Value)
        {
            public static Tag Make(string value) => new(value);

            internal static Tag Make(bool flag) => new(flag ? "yes" : "no");
        }
        """;

    /// <summary>An untyped stand-in for <c>metano-runtime</c>.</summary>
    private const string RuntimeStub = "declare module \"metano-runtime\";\n";

    private const string Consumer = """
        [assembly: TranspileAssembly]

        namespace App;

        using Acme;

        public class Wallet
        {
            public Money Balance { get; set; } = Money.FromCents(5);

            public Money Add(int cents) => Money.From(cents);

            public Money Parse(string text) => Money.From(text);

            public int Total() => Balance;
        }
        """;

    [Test]
    public async Task CrossPackageCompanion_TypeChecksUnderStrict()
    {
        var library = TranspileHelper.Transpile(Library);
        var consumer = TranspileHelper.TranspileWithLibrary(Library, Consumer);

        await Assert.That(library["acme/money.ts"]).Contains("export const Money = {");
        await Assert.That(consumer["app/wallet.ts"]).Contains("from \"acme-lib/acme\"");

        var root = Directory.CreateTempSubdirectory("metano-companion-strict-").FullName;

        try
        {
            WriteFiles(Path.Combine(root, "lib"), library);
            WriteFiles(Path.Combine(root, "app"), consumer);
            File.WriteAllText(Path.Combine(root, "tsconfig.json"), TsConfig());

            var (exitCode, output) = await RunTsgo(root);

            await Assert.That(output).IsEmpty();
            await Assert.That(exitCode).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InternalCompanionMember_IsStrippedFromTheDeclarations()
    {
        var library = TranspileHelper.Transpile(TaggedLibrary);
        var root = Directory.CreateTempSubdirectory("metano-companion-dts-").FullName;

        try
        {
            WriteFiles(Path.Combine(root, "lib"), library);
            File.WriteAllText(Path.Combine(root, "lib", "runtime.d.ts"), RuntimeStub);
            File.WriteAllText(Path.Combine(root, "tsconfig.json"), DeclarationTsConfig());

            var (exitCode, output) = await RunTsgo(root);

            await Assert.That(output).IsEmpty();
            await Assert.That(exitCode).IsEqualTo(0);

            var declarations = File.ReadAllText(Path.Combine(root, "out", "acme", "tag.d.ts"));

            await Assert.That(declarations).Contains("readonly make: typeof Tag$make;");
            await Assert.That(declarations).DoesNotContain("readonly makeFlag:");
            await Assert.That(declarations).DoesNotContain("flag: boolean");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteFiles(string directory, IReadOnlyDictionary<string, string> files)
    {
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(directory, name);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private static string TsConfig()
    {
        var runtimeSource = Path.Combine(RuntimeDirectory(), "src").Replace('\\', '/');

        return $$"""
            {
              "compilerOptions": {
                "strict": true,
                "noEmit": true,
                "target": "es2022",
                "lib": ["ESNext", "DOM"],
                "module": "esnext",
                "moduleResolution": "bundler",
                "allowImportingTsExtensions": true,
                "skipLibCheck": true,
                "types": [],
                "paths": {
                  "acme-lib/*": ["./lib/*", "./lib/*/index.ts"],
                  "metano-runtime": ["{{runtimeSource}}/index.ts"],
                  "metano-runtime/*": ["{{runtimeSource}}/*.ts", "{{runtimeSource}}/*/index.ts"],
                  "#/*": ["./app/*"]
                }
              },
              "include": ["app/**/*.ts", "lib/**/*.ts"]
            }
            """;
    }

    /// <summary>
    /// The library alone, emitting declarations under <c>stripInternal</c>. The
    /// runtime is the shorthand module <see cref="RuntimeStub"/>, so every source
    /// sits under <c>lib/</c> and the output mirrors it.
    /// </summary>
    private static string DeclarationTsConfig() =>
        """
            {
              "compilerOptions": {
                "strict": true,
                "declaration": true,
                "emitDeclarationOnly": true,
                "stripInternal": true,
                "outDir": "./out",
                "rootDir": "./lib",
                "target": "es2022",
                "module": "esnext",
                "moduleResolution": "bundler",
                "allowImportingTsExtensions": true,
                "skipLibCheck": true,
                "types": []
              },
              "include": ["lib/**/*.ts"]
            }
            """;

    private static string RuntimeDirectory() =>
        Path.Combine(TranspileHelper.RepositoryRoot(), "targets", "js", "metano-runtime");

    private static async Task<(int ExitCode, string Output)> RunTsgo(string root)
    {
        var tsgo = new[]
        {
            Path.Combine(RuntimeDirectory(), "node_modules", ".bin", "tsgo"),
            Path.Combine(TranspileHelper.RepositoryRoot(), "node_modules", ".bin", "tsgo"),
        }.First(File.Exists);

        var start = new ProcessStartInfo(tsgo, ["-p", root])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("tsgo did not start");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, (await output + await error).Trim());
    }
}
