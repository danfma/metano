import { afterEach, describe, expect, test } from "bun:test";
import {
  UnionGuardRegistry,
  getUnionGuard,
  registerUnionGuard,
} from "#/system/json/union-guard-registry.ts";

// Clean the registry between tests so cases stay independent — the
// registry is a module-level singleton in real usage (variant modules
// register once at load time), but the tests exercise different
// hierarchies.
afterEach(() => {
  UnionGuardRegistry.clear();
});

describe("UnionGuardRegistry", () => {
  test("registerUnionGuard stores the variant guard under its discriminator", () => {
    const isCircle = (v: unknown) =>
      typeof v === "object" &&
      v !== null &&
      (v as { kind?: string }).kind === "Circle" &&
      typeof (v as { radius?: unknown }).radius === "number";

    registerUnionGuard("Circle", isCircle);

    expect(getUnionGuard("Circle")).toBe(isCircle);
  });

  test("getUnionGuard returns undefined when no variant is registered", () => {
    expect(getUnionGuard("Triangle")).toBeUndefined();
  });

  test("simulates the [StrictUnionGuard] dispatch — full shape validation", () => {
    // Stand-ins for the generated variant guards. The base guard
    // does: `guard !== undefined ? guard(value) : true`.
    const isCircle = (v: unknown) => {
      const o = v as { kind?: string; radius?: unknown };
      return o.kind === "Circle" && typeof o.radius === "number";
    };
    const isSquare = (v: unknown) => {
      const o = v as { kind?: string; side?: unknown };
      return o.kind === "Square" && typeof o.side === "number";
    };
    registerUnionGuard("Circle", isCircle);
    registerUnionGuard("Square", isSquare);

    // Generated isShape body, transcribed:
    const isShape = (value: unknown): boolean => {
      if (value == null || typeof value !== "object") return false;
      const v = value as { kind?: string };
      if (!(v.kind === "Circle" || v.kind === "Square")) return false;
      const guard = getUnionGuard(v.kind);
      return guard !== undefined ? guard(value) : true;
    };

    expect(isShape({ kind: "Circle", radius: 1 })).toBe(true);
    expect(isShape({ kind: "Square", side: 2 })).toBe(true);
    // The soundness gap #88 left behind: discriminator alone is no
    // longer enough — missing `radius` now fails the strict path.
    expect(isShape({ kind: "Circle" })).toBe(false);
    expect(isShape({ kind: "Square" })).toBe(false);
    // Unknown discriminator still rejected.
    expect(isShape({ kind: "Triangle" })).toBe(false);
    // Non-object / null shapes still rejected.
    expect(isShape(null)).toBe(false);
    expect(isShape(42)).toBe(false);
  });

  test("fallback path: no variant registered → guard accepts the discriminator-only narrow", () => {
    // Same `isShape` body, but with no variants registered (variant
    // modules not loaded).
    const isShape = (value: unknown): boolean => {
      if (value == null || typeof value !== "object") return false;
      const v = value as { kind?: string };
      if (!(v.kind === "Circle" || v.kind === "Square")) return false;
      const guard = getUnionGuard(v.kind);
      return guard !== undefined ? guard(value) : true;
    };

    // Strict guard is never *more* restrictive than the legacy
    // discriminator-only narrow when no variants are loaded — the
    // contract that makes the opt-in safe.
    expect(isShape({ kind: "Circle" })).toBe(true);
    expect(isShape({ kind: "Square" })).toBe(true);
    expect(isShape({ kind: "Triangle" })).toBe(false);
  });

  test("registerUnionGuard is idempotent and replaces the previous entry", () => {
    const first = () => true;
    const second = () => false;

    registerUnionGuard("Circle", first);
    expect(getUnionGuard("Circle")).toBe(first);

    registerUnionGuard("Circle", second);
    expect(getUnionGuard("Circle")).toBe(second);
  });
});
