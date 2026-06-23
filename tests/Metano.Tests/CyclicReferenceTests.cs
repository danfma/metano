using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for the cyclic-reference detector that walks the generated TS files'
/// <c>#/</c> imports and emits an MS0005 warning per distinct cycle.
/// </summary>
public class CyclicReferenceTests
{
    [Test]
    public async Task NoCycle_NoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo
            {
                [Transpile]
                public class A
                {
                    public B? Friend { get; set; }
                }

                [Transpile]
                public class B
                {
                    public string Name { get; set; } = "";
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.CyclicReference))
            .IsFalse();
    }

    [Test]
    public async Task TwoFileCycle_EmitsMs0005Warning()
    {
        // A references B (via property), B references A (via property) → cycle.
        // Both are in the same namespace so the import paths resolve to the same folder.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo
            {
                [Transpile]
                public class Node
                {
                    public Edge? OutgoingEdge { get; set; }
                }

                [Transpile]
                public class Edge
                {
                    public Node? Target { get; set; }
                }
            }
            """
        );

        var cycle = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.CyclicReference);
        await Assert.That(cycle).IsNotNull();
        await Assert.That(cycle!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        // The cycle message should mention both files.
        await Assert.That(cycle.Message).Contains("node");
        await Assert.That(cycle.Message).Contains("edge");
    }

    [Test]
    public async Task TwoFileCycle_UnderImportAlias_StillDetectedAndRendersAliasKey()
    {
        // The same Node ↔ Edge cycle is still detected when a custom import alias is
        // configured. Both types share the root namespace, so their real imports are
        // relative (`./edge`, `./node`); the cycle renderer synthesizes the alias prefix
        // for display, which is what this asserts (exercising the alias-aware display).
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo
            {
                [Transpile]
                public class Node
                {
                    public Edge? OutgoingEdge { get; set; }
                }

                [Transpile]
                public class Edge
                {
                    public Node? Target { get; set; }
                }
            }
            """,
            importAlias: "contracts"
        );

        var cycle = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.CyclicReference);
        await Assert.That(cycle).IsNotNull();
        await Assert.That(cycle!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        // The diagnostic message reflects the configured alias key, not the default `#`.
        await Assert.That(cycle.Message).Contains("#contracts/");
    }

    [Test]
    public async Task SelfReferenceWithoutPropertyImport_NoDiagnostic()
    {
        // A type that references its own type name doesn't create a file-level import
        // (the file declares the class — no import needed for itself).
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo
            {
                [Transpile]
                public class TreeNode
                {
                    public TreeNode? Parent { get; set; }
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.CyclicReference))
            .IsFalse();
    }
}
