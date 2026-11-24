using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Tests;

/// <summary>
/// Tests for the alias normalization of <c>--import-alias</c> and for the relative
/// <c>.js</c> specifiers generated code uses whatever the alias (spec 004 Amendment
/// A1): the unit-level shaping in <see cref="ModuleSpecifierRenderer"/> and the
/// end-to-end emitted imports. The package.json side is covered in
/// <see cref="EmitPackageTests"/>.
/// </summary>
public class ImportAliasTests
{
    private static readonly PathNaming AppNaming = new("App");

    // ── Alias normalization (FR-007) ─────────────────────────────────

    [Test]
    [Arguments("contracts", "#contracts")]
    [Arguments("#contracts", "#contracts")]
    [Arguments("  contracts  ", "#contracts")]
    [Arguments(null, "#")]
    [Arguments("", "#")]
    [Arguments("   ", "#")]
    [Arguments("#", "#")]
    public async Task NormalizeImportAliasPrefix_NormalizesValue(string? input, string expected)
    {
        await Assert.That(PathNaming.NormalizeImportAliasPrefix(input)).IsEqualTo(expected);
    }

    // ── Relative `.js` specifiers (spec 004 Amendment A1, FR-011) ────

    private static string Render(string fromNs, string toNs, string fileName) =>
        ModuleSpecifierRenderer.Default.Render(
            AppNaming.LocalModule(toNs, fileName),
            AppNaming.FileKey(fromNs, "Importer")
        );

    [Test]
    [Arguments("App.Models", "App.Models", "Money", "./money.js")]
    [Arguments("App.Services", "App.Models", "Money", "../models/money.js")]
    [Arguments("App.Services", "App", "Root", "../root.js")]
    [Arguments("App", "App.Models.Deep", "Money", "./models/deep/money.js")]
    [Arguments("App.A.B.C", "App.A.X", "Money", "../../x/money.js")]
    [Arguments("App.Ab", "App.A", "Money", "../a/money.js")]
    public async Task ModuleSpecifier_IsRelativeToTheImporterWithAJsExtension(
        string fromNs,
        string toNs,
        string fileName,
        string expected
    )
    {
        await Assert.That(Render(fromNs, toNs, fileName)).IsEqualTo(expected);
    }

    [Test]
    public async Task ModuleSpecifier_Barrel_NamesItsIndexFile()
    {
        var specifier = ModuleSpecifierRenderer.Default.Render(
            new TsLocalModule("app/domain/index"),
            "index"
        );

        await Assert.That(specifier).IsEqualTo("./app/domain/index.js");
    }

    [Test]
    public async Task ModuleSpecifier_PackageModule_PrintsVerbatim()
    {
        var specifier = ModuleSpecifierRenderer.Default.Render(
            new TsPackageModule("pkg/sub"),
            "a/b"
        );

        await Assert.That(specifier).IsEqualTo("pkg/sub");
    }

    // ── End-to-end emitted imports (FR-011) ──────────────────────────

    private const string TwoNamespaceSource = """
        namespace App.Models
        {
            [Transpile]
            public class Money
            {
                public decimal Amount { get; set; }
            }
        }

        namespace App.Services
        {
            [Transpile]
            public class Wallet
            {
                public App.Models.Money Balance { get; set; } = new();
            }
        }
        """;

    [Test]
    public async Task Transpile_EmitsRelativeJsInternalImports()
    {
        var files = TranspileHelper.Transpile(TwoNamespaceSource);

        await Assert.That(files["app/services/wallet.ts"]).Contains("from \"../models/money.js\"");

        // No subpath-alias specifier is emitted for an internal edge.
        await Assert.That(files.Values.Any(c => c.Contains("from \"#"))).IsFalse();
    }
}
