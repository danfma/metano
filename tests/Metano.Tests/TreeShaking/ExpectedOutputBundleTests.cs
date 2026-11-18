using System.Diagnostics;
using System.Text.Json;

namespace Metano.Tests.TreeShaking;

/// <summary>
/// The golden bundle check of spec 011 FR-043: every golden, compiled with tsgo
/// and bundled as a side-effect-only import by bun, esbuild and Rollup with every
/// import external, keeps no code evaluated at module load. The tooling is the
/// runtime's <c>test/tree-shaking/golden-bundle.ts</c>; this test owns the
/// verdict. <c>allowlist.txt</c> names the goldens that still keep code (it only
/// shrinks), and <c>entry-points.txt</c> the exempt <c>[ModuleEntryPoint]</c>
/// goldens.
/// </summary>
public class ExpectedOutputBundleTests
{
    /// <summary>
    /// The most <c>allowlist.txt</c> may ever hold: the measured state when the
    /// check landed. Growing the list means growing this set too.
    /// </summary>
    private static readonly IReadOnlySet<string> MaximalAllowlist = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "bcl-exception-identity.ts",
        "branded-value-object.ts",
        "companion-direct-overloads-key.ts",
        "companion-direct-overloads-operators.ts",
        "companion-direct-overloads-renamed.ts",
        "companion-direct-overloads-tag.ts",
        "dispatcher-defaults-imports.ts",
        "dispatcher-defaults-params-branded.ts",
        "dispatcher-defaults-params-constructor.ts",
        "dispatcher-generic-constrained.ts",
        "dispatcher-generic-mixed.ts",
        "dispatcher-generic-unconstrained.ts",
        "exception-overload-message-override.ts",
        "int64-json-context.ts",
        "json-closure-generic-context.ts",
        "json-context-closure.ts",
        "json-context-factory.ts",
        "json-context-null-roots.ts",
        "json-context-value-roots.ts",
        "json-dictionary-keys.ts",
        "json-member-options-context.ts",
        "json-polymorphic-context.ts",
        "json-structural-polymorphic-context.ts",
        "json-web-context.ts",
        "lazy-static-constructor-field-order.ts",
        "lazy-static-constructor-struct-dispatched.ts",
        "lazy-static-constructor-struct-params.ts",
        "lazy-static-constructor-struct.ts",
        "lazy-static-constructor.ts",
        "mixed-visibility-overloads.ts",
        "named-argument-order.ts",
        "native-int-json-context.ts",
        "nested-types-static-class.ts",
        "no-container-module-state.ts",
        "operator-helper-branded.ts",
        "overload-calls-nested.ts",
        "overload-calls-super.ts",
        "overload-dispatcher-fallback.ts",
        "overload-factory-abstract.ts",
        "overload-factory-accessibility.ts",
        "overload-factory-basic.ts",
        "overload-factory-chain.ts",
        "overload-factory-cross-package.ts",
        "overload-factory-generic-closed.ts",
        "overload-factory-generic-constraints.ts",
        "overload-factory-generic.ts",
        "overload-factory-hierarchy-generic.ts",
        "overload-factory-hierarchy.ts",
        "overload-factory-params.ts",
        "overload-factory-record.ts",
        "overload-factory-struct-defaults.ts",
        "overload-factory-struct.ts",
        "overload-json-factory.ts",
        "overload-name-pin-claimed.ts",
        "overload-name-pin-class.ts",
        "overload-name-pin-deterministic.ts",
        "overload-name-pin-json-factory.ts",
        "overload-names-claims.ts",
        "overload-static-init-cycle.ts",
        "overload-static-init.ts",
        "primary-ctor-dispatch-captured.ts",
        "primary-ctor-dispatch-computed.ts",
        "primary-ctor-dispatch-initializer.ts",
        "static-constructor-operators-derived.ts",
        "static-constructor-operators-struct.ts",
        "static-constructor-operators.ts",
    };

    private static readonly Lazy<Task<BundleReport>> Report = new(RunGoldenBundleCheck);

    [Test]
    public async Task TheCanaryIsKeptByEveryBundler()
    {
        var report = await Report.Value;

        await Assert.That(report.Canary.Values).All().Satisfy(code => code.Contains("register"));
        await Assert.That(report.Canary.Count).IsEqualTo(3);
    }

    [Test]
    public async Task EveryGoldenIsChecked()
    {
        var report = await Report.Value;

        await Assert.That(report.Errors).IsEmpty();
    }

    [Test]
    public async Task NoUnlistedGoldenKeepsModuleLoadCode()
    {
        var report = await Report.Value;
        var exempt = ReadList("allowlist.txt").Concat(ReadList("entry-points.txt")).ToHashSet();

        await Assert
            .That(report.Violations.Keys.Where(golden => !exempt.Contains(golden)))
            .IsEmpty();
    }

    [Test]
    public async Task EveryListedGoldenStillKeepsModuleLoadCode()
    {
        var report = await Report.Value;
        var listed = ReadList("allowlist.txt").Concat(ReadList("entry-points.txt"));

        await Assert.That(listed.Where(golden => !report.Violations.ContainsKey(golden))).IsEmpty();
    }

    [Test]
    public async Task TheAllowlistNeverGrowsBeyondItsMaximalSet()
    {
        var allowlist = ReadList("allowlist.txt");

        await Assert.That(allowlist.Where(golden => !MaximalAllowlist.Contains(golden))).IsEmpty();

        await Assert
            .That(allowlist)
            .IsEquivalentTo(allowlist.Distinct().Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task EveryExemptGoldenComesFromAModuleEntryPoint()
    {
        var sources = TestSources();

        var withoutEntryPoint = ReadList("entry-points.txt")
            .Where(golden => !ComesFromAModuleEntryPoint(golden, sources))
            .ToList();

        await Assert.That(withoutEntryPoint).IsEmpty();
    }

    /// <summary>
    /// Whether a test source that names <paramref name="golden"/> declares a
    /// <c>[ModuleEntryPoint]</c> method.
    /// </summary>
    private static bool ComesFromAModuleEntryPoint(string golden, IReadOnlyList<string> sources)
    {
        var naming = sources.Where(source =>
            source.Contains($"\"{golden}\"", StringComparison.Ordinal)
        );

        return naming.Any(source =>
            source.Contains("[ModuleEntryPoint]", StringComparison.Ordinal)
        );
    }

    private static IReadOnlyList<string> TestSources() =>
        [
            .. Directory
                .EnumerateFiles(TestProjectDirectory(), "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText),
        ];

    private static IReadOnlyList<string> ReadList(string fileName) =>
        [
            .. File.ReadAllLines(Path.Combine(TestProjectDirectory(), "TreeShaking", fileName))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#')),
        ];

    private static string TestProjectDirectory() =>
        Path.Combine(TranspileHelper.RepositoryRoot(), "tests", "Metano.Tests");

    private static async Task<BundleReport> RunGoldenBundleCheck()
    {
        var runtime = Path.Combine(
            TranspileHelper.RepositoryRoot(),
            "targets",
            "js",
            "metano-runtime"
        );

        var script = Path.Combine(runtime, "test", "tree-shaking", "golden-bundle.ts");

        RequireRuntimeDependencies(runtime);

        var reportFile = Path.GetTempFileName();

        try
        {
            var start = new ProcessStartInfo(
                "bun",
                [script, TranspileHelper.SourceGoldenDirectory(), reportFile]
            )
            {
                WorkingDirectory = runtime,
                RedirectStandardError = true,
            };

            using var process = StartBun(start);

            var error = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"golden-bundle.ts exited with {process.ExitCode}: {await error}"
                );
            }

            return BundleReport.Parse(await File.ReadAllTextAsync(reportFile));
        }
        finally
        {
            File.Delete(reportFile);
        }
    }

    /// <summary>
    /// The runtime's dev dependencies the tooling loads; a checkout without
    /// <c>bun install</c> fails here with the fix rather than deep in bun.
    /// </summary>
    private static void RequireRuntimeDependencies(string runtime)
    {
        string[] required =
        [
            "esbuild",
            "rollup",
            "@rollup/plugin-terser",
            "@typescript/native-preview",
        ];

        // bun installs a workspace package's dependency beside it or hoists it to the root.
        string[] nodeModules =
        [
            Path.Combine(runtime, "node_modules"),
            Path.Combine(TranspileHelper.RepositoryRoot(), "node_modules"),
        ];

        var missing = required
            .Where(package =>
                !nodeModules.Any(directory => Directory.Exists(Path.Combine(directory, package)))
            )
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The golden bundle check needs metano-runtime's dev dependencies "
                    + $"({string.Join(", ", missing)} missing): run 'bun install' at the repository root."
            );
        }
    }

    private static Process StartBun(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException("bun did not start");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "The golden bundle check runs bun, which is not on PATH: install it from https://bun.sh.",
                exception
            );
        }
    }

    /// <summary>
    /// What the tooling found: the canary's kept code per bundler, the goldens
    /// that keep module-load code (with the code per bundler), and the goldens
    /// it could not check.
    /// </summary>
    private sealed record BundleReport(
        IReadOnlyDictionary<string, string> Canary,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Violations,
        IReadOnlyDictionary<string, string> Errors
    )
    {
        public static BundleReport Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var violations = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            var errors = new Dictionary<string, string>();

            foreach (var golden in root.GetProperty("goldens").EnumerateObject())
            {
                if (golden.Value.TryGetProperty("error", out var failure))
                {
                    errors[golden.Name] = failure.GetString() ?? "";

                    continue;
                }

                var kept = CodePerBundler(golden.Value);

                if (kept.Values.Any(code => code.Length > 0))
                    violations[golden.Name] = kept;
            }

            return new BundleReport(CodePerBundler(root.GetProperty("canary")), violations, errors);
        }

        private static IReadOnlyDictionary<string, string> CodePerBundler(JsonElement element) =>
            element
                .EnumerateObject()
                .ToDictionary(bundler => bundler.Name, bundler => bundler.Value.GetString() ?? "");
    }
}
