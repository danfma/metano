using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Tests;

/// <summary>
/// Tests for the configurable isolated subpath-import alias (<c>--import-alias</c>).
/// Covers both the unit-level specifier shaping in <see cref="ModuleSpecifierRenderer"/> and the
/// end-to-end emitted imports, plus the no-regression guarantee when no alias is set.
/// The package.json side is covered in <see cref="EmitPackageTests"/>.
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

    // ── In-file specifier with an alias (FR-002) ─────────────────────

    private static string Render(string? alias, string fromNs, string toNs, string fileName) =>
        new ModuleSpecifierRenderer(alias).Render(
            AppNaming.LocalModule(toNs, fileName),
            AppNaming.FileKey(fromNs, "Importer")
        );

    [Test]
    public async Task ModuleSpecifier_WithAlias_UsesAliasKey()
    {
        // Cross-namespace → alias-scoped path to the defining file.
        await Assert
            .That(Render("contracts", "App.Services", "App.Models", "Money"))
            .IsEqualTo("#contracts/models/money");

        // Root namespace → alias key + file name.
        await Assert
            .That(Render("contracts", "App.Services", "App", "Root"))
            .IsEqualTo("#contracts/root");

        // Same namespace → relative file import, never aliased.
        await Assert
            .That(Render("contracts", "App.Models", "App.Models", "Money"))
            .IsEqualTo("./money");
    }

    [Test]
    public async Task ModuleSpecifier_AliasNormalized_LeadingHashEquivalent()
    {
        await Assert
            .That(Render("#contracts", "App.A", "App.B", "T"))
            .IsEqualTo(Render("contracts", "App.A", "App.B", "T"));
    }

    [Test]
    public async Task ModuleSpecifier_BarrelBelowImporter_RendersItsDirectory()
    {
        var specifier = new ModuleSpecifierRenderer(null).Render(
            new TsLocalModule("app/domain/index"),
            "index"
        );

        await Assert.That(specifier).IsEqualTo("./app/domain");
    }

    [Test]
    public async Task ModuleSpecifier_PackageModule_PrintsVerbatim()
    {
        var specifier = new ModuleSpecifierRenderer("contracts").Render(
            new TsPackageModule("pkg/sub"),
            "a/b"
        );

        await Assert.That(specifier).IsEqualTo("pkg/sub");
    }

    // ── No-regression when unset (FR-004 / SC-002) ───────────────────

    [Test]
    public async Task ModuleSpecifier_NoAlias_UsesDefaultHashKey()
    {
        await Assert
            .That(Render(null, "App.Services", "App.Models", "Money"))
            .IsEqualTo("#/models/money");

        await Assert.That(Render(null, "App.Services", "App", "Root")).IsEqualTo("#/root");
        await Assert.That(Render(null, "App.Models", "App.Models", "Money")).IsEqualTo("./money");
    }

    // ── End-to-end emitted imports (FR-002) ──────────────────────────

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
    public async Task Transpile_WithAlias_EmitsAliasScopedInternalImports()
    {
        var files = TranspileHelper.Transpile(TwoNamespaceSource, importAlias: "contracts");

        // The cross-namespace reference resolves through the isolated alias key,
        // pointing at the defining file under its full namespace.
        await Assert
            .That(files.Values.Any(c => c.Contains("from \"#contracts/app/models/money\"")))
            .IsTrue();

        // The default `#` alias never appears.
        await Assert.That(files.Values.Any(c => c.Contains("from \"#/"))).IsFalse();
        await Assert.That(files.Values.Any(c => c.Contains("from \"#\""))).IsFalse();
    }

    [Test]
    public async Task Transpile_NoAlias_EmitsDefaultHashImports()
    {
        var files = TranspileHelper.Transpile(TwoNamespaceSource);

        // Default behavior: internal imports use `#/...` pointing at the file
        // under its full namespace.
        await Assert
            .That(files.Values.Any(c => c.Contains("from \"#/app/models/money\"")))
            .IsTrue();

        // No alias-scoped key leaks in.
        await Assert.That(files.Values.Any(c => c.Contains("#contracts"))).IsFalse();
    }
}
