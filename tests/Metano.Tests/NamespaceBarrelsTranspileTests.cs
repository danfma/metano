namespace Metano.Tests;

/// <summary>
/// Covers the <c>--namespace-barrels</c> opt-in. Shipping types across
/// sub-namespaces produces one leaf <c>index.ts</c> per directory by
/// default; with the flag set, <see cref="Metano.Compiler.TypeScript.Transformation.BarrelFileGenerator"/>
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

        // Full-namespace layout: each leaf directory gets a namespace import
        // aliased as the underscore-joined PascalCased FULL namespace path.
        await Assert
            .That(root)
            .Contains("import * as $App_Issues_Domain from \"./app/issues/domain\"");
        await Assert
            .That(root)
            .Contains("import * as $App_Planning_Domain from \"./app/planning/domain\"");

        // Nested namespace blocks mirror the C# tree, rooted at App.
        await Assert.That(root).Contains("export namespace App");
        await Assert.That(root).Contains("export namespace Issues");
        await Assert.That(root).Contains("export namespace Planning");
        await Assert.That(root).Contains("export import Domain = $App_Issues_Domain");
        await Assert.That(root).Contains("export import Domain = $App_Planning_Domain");
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

        await Assert
            .That(root)
            .Contains("import * as $App_SharedKernel from \"./app/shared-kernel\"");
        await Assert.That(root).Contains("export import SharedKernel = $App_SharedKernel;");
        await Assert.That(root).DoesNotContain("export namespace SharedKernel");
    }

    [Test]
    public async Task NamespaceBarrels_MergesWithBareRootLeaf()
    {
        // Project with both an App-root type and sub-namespaces. In the
        // full-namespace layout the App namespace is itself a leaf
        // directory: `app/index.ts` re-exports the App-root type, and the
        // root `index.ts` aggregates every leaf — including `./app` — under
        // nested `export namespace` blocks.
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
        // The App leaf re-export lives in its own directory barrel.
        await Assert.That(result).ContainsKey("app/index.ts");
        await Assert.That(result["app/index.ts"]).Contains("export { Root } from \"./root\"");
        // The root aggregates the App leaf plus the sub-namespace.
        await Assert.That(root).Contains("import * as $App from \"./app\"");
        await Assert
            .That(root)
            .Contains("import * as $App_Issues_Domain from \"./app/issues/domain\"");
        await Assert.That(root).Contains("export namespace App");
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

        await Assert.That(result).ContainsKey("app/issues/domain/index.ts");
        await Assert.That(result).ContainsKey("index.ts");
    }
}
