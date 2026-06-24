import { describe, expect, test } from "bun:test";

// SampleIssueTracker opts into the `--namespace-barrels` root barrel
// (see its .csproj `MetanoNamespaceBarrels` property). The emitted
// `src/index.ts` aggregates every leaf barrel under nested
// `export namespace` blocks mirroring the C# namespace hierarchy.
// Under the full-namespace layout (ADR-0025) that hierarchy is rooted
// at the package's own namespace, so the tree starts at
// `SampleIssueTracker`. This test exercises root-level imports so a
// regression that drops the namespace-aggregation emission surfaces as
// a type error at build time and a runtime failure here.

// Uses the `#` self-alias (`./src/index.ts`) instead of the external
// package specifier — Bun workspace self-referencing is fragile for
// tests and the `#` alias resolves to the same barrel either way.
import { SampleIssueTracker } from "#";

describe("namespace-barrels root access", () => {
  test("Issues namespace exposes Application + Domain branches", () => {
    expect(typeof SampleIssueTracker.Issues.Application).toBe("object");
    expect(typeof SampleIssueTracker.Issues.Domain).toBe("object");
  });

  test("Planning namespace exposes Domain branch", () => {
    expect(typeof SampleIssueTracker.Planning.Domain).toBe("object");
  });

  test("SharedKernel is bound under the package namespace", () => {
    // Single-segment leaves collapse to a bare `export import` inside the
    // package namespace block — consumers get the namespace object
    // directly without a deeper wrapping block.
    expect(typeof SampleIssueTracker.SharedKernel).toBe("object");
  });

  test("nested enum is reachable through the namespace tree", () => {
    // Issues.Domain.IssuePriority is a [StringEnum] — values survive
    // the re-export chain. Literal values carry the lowercase override
    // from the C# side.
    const priority = SampleIssueTracker.Issues.Domain.IssuePriority.High;
    expect(priority).toBe("high");
  });
});
