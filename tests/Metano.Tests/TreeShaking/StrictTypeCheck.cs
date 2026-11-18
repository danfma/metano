using System.Diagnostics;

namespace Metano.Tests.TreeShaking;

/// <summary>
/// Type-checks transpiled packages with <c>tsgo --strict --noImplicitOverride</c> in a
/// scratch directory:
/// a library under <c>lib/</c> (resolved as <c>acme-lib/*</c>), a consumer under
/// <c>app/</c>, and the runtime from its sources.
/// </summary>
internal static class StrictTypeCheck
{
    /// <summary>
    /// Writes <paramref name="library"/> and <paramref name="consumer"/> and runs
    /// <c>tsgo</c> over both; returns its exit code and output.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> Run(
        IReadOnlyDictionary<string, string> library,
        IReadOnlyDictionary<string, string> consumer
    )
    {
        var root = Directory.CreateTempSubdirectory("metano-strict-").FullName;

        try
        {
            WriteFiles(Path.Combine(root, "lib"), library);
            WriteFiles(Path.Combine(root, "app"), consumer);
            File.WriteAllText(Path.Combine(root, "tsconfig.json"), TsConfig());

            return await RunTsgo(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static void WriteFiles(string directory, IReadOnlyDictionary<string, string> files)
    {
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(directory, name);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    public static async Task<(int ExitCode, string Output)> RunTsgo(string root)
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

    private static string TsConfig()
    {
        var runtimeSource = Path.Combine(RuntimeDirectory(), "src").Replace('\\', '/');

        return $$"""
            {
              "compilerOptions": {
                "strict": true,
                "noImplicitOverride": true,
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

    private static string RuntimeDirectory() =>
        Path.Combine(TranspileHelper.RepositoryRoot(), "targets", "js", "metano-runtime");
}
