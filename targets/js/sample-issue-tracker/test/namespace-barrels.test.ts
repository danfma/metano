import { describe, expect, test } from "bun:test";

// SampleIssueTracker opts into the `--namespace-barrels` root barrel
// (see its .csproj `MetanoNamespaceBarrels` property). The emitted
// `src/index.ts` aggregates every leaf barrel under nested
// `export namespace` blocks mirroring the C# namespace hierarchy.
// This test exercises root-level imports so a regression that drops
// the namespace-aggregation emission surfaces as a type error at
// build time and a runtime failure here.

// Uses the `#` self-alias (`./src/index.ts`) instead of the external
// package specifier — Bun workspace self-referencing is fragile for
// tests and the `#` alias resolves to the same barrel either way.
import { Issues, Planning, SharedKernel } from "#";

describe("namespace-barrels root access", () => {
  test("Issues namespace exposes Application + Domain branches", () => {
    expect(typeof Issues.Application).toBe("object");
    expect(typeof Issues.Domain).toBe("object");
  });

  test("Planning namespace exposes Domain branch", () => {
    expect(typeof Planning.Domain).toBe("object");
  });

  test("SharedKernel is bound at the package root", () => {
    // Single-segment leaves collapse to a bare `export import` at the
    // root — consumers get the namespace object directly without a
    // wrapping block.
    expect(typeof SharedKernel).toBe("object");
  });

  test("nested enum is reachable through the namespace tree", () => {
    // Issues.Domain.IssuePriority is a [StringEnum] — values survive
    // the re-export chain. Literal values carry the lowercase override
    // from the C# side.
    const priority = Issues.Domain.IssuePriority.High;
    expect(priority).toBe("high");
  });
});
