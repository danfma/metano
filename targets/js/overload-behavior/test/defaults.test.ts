import { describe, expect, test } from "bun:test";
// Relative import: the subpath-alias convention is for the sample apps, and
// the int64 conformance package sets the precedent of relative imports for
// a cross-runner behavior probe (Node 24 rejects `#/` specifiers).
import { Caller } from "../src/caller";

// DECIDED-32, executable: the static receiver type's overload supplies an
// omitted optional argument's default. `BaseGreeter.Greet(string, int = 3)`
// and the hiding `LoudGreeter.Greet(string, int = 9)` — through the
// base-typed receiver the emitted direct call passes 3 (the .NET probe's
// exact result), on the derived receiver it passes 9 and returns 90.
describe("omitted optional defaults", () => {
  test("bind the static overload's default through the base receiver", () => {
    expect(Caller.throughBase()).toBe(3);
  });

  test("bind the derived overload's default on the direct call", () => {
    expect(Caller.direct()).toBe(90);
  });
});
