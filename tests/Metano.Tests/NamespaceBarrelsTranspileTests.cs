namespace Metano.Tests;

/// <summary>
/// Covers the <c>--namespace-barrels</c> opt-in. Shipping types across
/// sub-namespaces produces one leaf <c>index.ts</c> per directory by
/// default; with the flag set, <see cref="Metano.Transformation.BarrelFileGenerator"/>
/// additionally emits a root <c>src/index.ts</c> aggregating the leaves
/// under nested <c>export namespace</c> blocks. Tree-shaking stays
/// intact because each subpath is bound to a single namespace import —
/// no <c>export *</c> aggregation at the root (see ADR-0006).
/// </summary>
public class NamespaceBarrelsTranspileTests
{
    [Test]
    public async Task NamespaceBarrels_EmitsRootIndex_WhenSubNamespacesExist()
    {
        // Two sibling sub-namespaces (`Issues.Domain`, `Planning.Domain`)
        // with no bare-root types exercises the root-barrel emission.
        // Without the flag there's no `index.ts`; with the flag the
        // root aggregates both leaves under nested `export namespace`
        // blocks so `import { Issues, Planning } from "@pkg"` resolves.
        var result = TranspileHelper.TranspileWithNamespaceBarrels(
            """
            namespace App.Issues.Domain
            {
                [Transpile]
                public record Issue(string Id);
            }

            namespace App.Planning.Domain
            {
                [Transpile]
                public record Sprint(string Key);
            }
            """
        );

        await Assert.That(result).ContainsKey("index.ts");
        var root = result["index.ts"];

        // Each leaf directory gets a namespace import aliased as the
        // underscore-joined PascalCased path.
        await Assert.That(root).Contains("import * as $Issues_Domain from \"./issues/domain\"");
        await Assert.That(root).Contains("import * as $Planning_Domain from \"./planning/domain\"");

        // Nested namespace blocks mirror the C# tree.
        await Assert.That(root).Contains("export namespace Issues");
        await Assert.That(root).Contains("export namespace Planning");
        await Assert.That(root).Contains("export import Domain = $Issues_Domain");
        await Assert.That(root).Contains("export import Domain = $Planning_Domain");
    }

    [Test]
    public async Task NamespaceBarrels_FlattensTopLevelLeaf_WhenSingleSegment()
    {
        // A top-level single-segment leaf (like `SharedKernel`) doesn't
        // need an enclosing namespace block — it collapses to a bare
        // `export import SharedKernel = SharedKernel;` at the root so
        // the binding surfaces under the package root directly. Pairs
        // with a sibling `Issues` namespace to force the root to sit
        // at `App` instead of collapsing onto `App.SharedKernel`.
        var result = TranspileHelper.TranspileWithNamespaceBarrels(
            """
            namespace App.SharedKernel
            {
                [Transpile]
                public record OperationResult(bool Success);
            }

            namespace App.Issues.Domain
            {
                [Transpile]
                public record Issue(string Id);
            }
            """
        );

        await Assert.That(result).ContainsKey("index.ts");
        var root = result["index.ts"];

        await Assert.That(root).Contains("import * as $SharedKernel from \"./shared-kernel\"");
        await Assert.That(root).Contains("export import SharedKernel = $SharedKernel;");
        await Assert.That(root).DoesNotContain("export namespace SharedKernel");
    }

    [Test]
    public async Task NamespaceBarrels_MergesWithBareRootLeaf()
    {
        // Project with both a bare-root type and sub-namespaces: the
        // existing root leaf barrel (re-exporting the bare-root type)
        // gets the namespace-aggregation block appended, so both
        // `import { Root } from "@pkg"` and
        // `import { Issues } from "@pkg"` resolve from a single
        // index.ts.
        var result = TranspileHelper.TranspileWithNamespaceBarrels(
            """
            namespace App
            {
                [Transpile]
                public record Root(string Id);
            }

            namespace App.Issues.Domain
            {
                [Transpile]
                public record Issue(string Id);
            }
            """
        );

        await Assert.That(result).ContainsKey("index.ts");
        var root = result["index.ts"];
        // Bare-root leaf re-export stays.
        await Assert.That(root).Contains("export { Root } from \"./root\"");
        // Plus the namespace-aggregation block for the sub-namespace.
        await Assert.That(root).Contains("import * as $Issues_Domain from \"./issues/domain\"");
        await Assert.That(root).Contains("export namespace Issues");
    }

    [Test]
    public async Task NamespaceBarrels_OffByDefault_ProducesNoRootIndex()
    {
        // Baseline: without the flag, sub-namespace-only projects get no
        // root index — matches the pre-existing leaf-only default from
        // ADR-0006. Two sibling namespaces under `App` force the root
        // to sit at `App` without any types at the bare root.
        var result = TranspileHelper.Transpile(
            """
            namespace App.Issues.Domain
            {
                [Transpile]
                public record Issue(string Id);
            }

            namespace App.Planning.Domain
            {
                [Transpile]
                public record Sprint(string Key);
            }
            """
        );

        await Assert.That(result).DoesNotContainKey("index.ts");
    }

    [Test]
    public async Task NamespaceBarrels_PreservesLeafBarrels()
    {
        // The root aggregation is additive — leaf barrels still emit
        // under their own directories so consumers that import from
        // `@pkg/issues/domain` continue to work.
        var result = TranspileHelper.TranspileWithNamespaceBarrels(
            """
            namespace App.Issues.Domain
            {
                [Transpile]
                public record Issue(string Id);
            }

            namespace App.Planning.Domain
            {
                [Transpile]
                public record Sprint(string Key);
            }
            """
        );

        await Assert.That(result).ContainsKey("issues/domain/index.ts");
        await Assert.That(result).ContainsKey("index.ts");
    }
}
