using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Tests;

/// <summary>
/// Tests for the configurable isolated subpath-import alias (<c>--import-alias</c>).
/// Covers both the unit-level specifier shaping in <see cref="PathNaming"/> and the
/// end-to-end emitted imports, plus the no-regression guarantee when no alias is set.
/// The package.json side is covered in <see cref="EmitPackageTests"/>.
/// </summary>
public class ImportAliasTests
{
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

    [Test]
    public async Task ComputeRelativeImportPath_WithAlias_UsesAliasKey()
    {
        var naming = new PathNaming("App", "contracts");

        // Cross-namespace → alias-scoped barrel.
        await Assert
            .That(naming.ComputeRelativeImportPath("App.Services", "App.Models", "Money"))
            .IsEqualTo("#contracts/models");
        // Root namespace → bare alias key.
        await Assert
            .That(naming.ComputeRelativeImportPath("App.Services", "App", "Root"))
            .IsEqualTo("#contracts");
        // Same namespace → relative file import, never aliased.
        await Assert
            .That(naming.ComputeRelativeImportPath("App.Models", "App.Models", "Money"))
            .IsEqualTo("./money");
    }

    [Test]
    public async Task ComputeRelativeImportPath_AliasNormalized_LeadingHashEquivalent()
    {
        var withHash = new PathNaming("App", "#contracts");
        var withoutHash = new PathNaming("App", "contracts");

        await Assert
            .That(withHash.ComputeRelativeImportPath("App.A", "App.B", "T"))
            .IsEqualTo(withoutHash.ComputeRelativeImportPath("App.A", "App.B", "T"));
    }

    // ── No-regression when unset (FR-004 / SC-002) ───────────────────

    [Test]
    public async Task ComputeRelativeImportPath_NoAlias_UsesDefaultHashKey()
    {
        var naming = new PathNaming("App");

        await Assert
            .That(naming.ComputeRelativeImportPath("App.Services", "App.Models", "Money"))
            .IsEqualTo("#/models");
        await Assert
            .That(naming.ComputeRelativeImportPath("App.Services", "App", "Root"))
            .IsEqualTo("#");
        await Assert
            .That(naming.ComputeRelativeImportPath("App.Models", "App.Models", "Money"))
            .IsEqualTo("./money");
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

        // The cross-namespace reference resolves through the isolated alias key.
        await Assert.That(files.Values.Any(c => c.Contains("from \"#contracts/models\""))).IsTrue();
        // The default `#` alias never appears.
        await Assert.That(files.Values.Any(c => c.Contains("from \"#/"))).IsFalse();
        await Assert.That(files.Values.Any(c => c.Contains("from \"#\""))).IsFalse();
    }

    [Test]
    public async Task Transpile_NoAlias_EmitsDefaultHashImports()
    {
        var files = TranspileHelper.Transpile(TwoNamespaceSource);

        // Default behavior is unchanged: internal imports use `#/...`.
        await Assert.That(files.Values.Any(c => c.Contains("from \"#/models\""))).IsTrue();
        // No alias-scoped key leaks in.
        await Assert.That(files.Values.Any(c => c.Contains("#contracts"))).IsFalse();
    }
}
