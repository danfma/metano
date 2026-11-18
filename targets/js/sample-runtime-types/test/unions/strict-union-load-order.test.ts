import { describe, expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { resolve } from "node:path";

const nodeScript = resolve(import.meta.dir, "strict-union-load-order.node.mjs");
const dist = resolve(import.meta.dir, "../../dist/sample-runtime-types/unions/token.guard.js");

describe("strict union guard module (FR-029)", () => {
  test("Bun: importing the base first, unbundled, then the guard works", async () => {
    const { Token } = await import("#/sample-runtime-types/unions/token");
    const { isToken } = await import("#/sample-runtime-types/unions/token.guard");

    expect(typeof Token).toBe("function");
    expect(isToken({ kind: "NumberToken", value: 1 })).toBe(true);
    expect(isToken({ kind: "NumberToken", value: "one" })).toBe(false);
    expect(isToken({ kind: "TextToken", text: "a" })).toBe(true);
    expect(isToken({ kind: "Unknown" })).toBe(false);
  });

  test("Node: importing the built base first, unbundled, then the guard works", () => {
    // `bun run build` writes dist; the gate builds before it tests.
    expect(existsSync(dist)).toBe(true);

    const run = spawnSync("node", [nodeScript], { encoding: "utf8" });

    expect(run.stderr).toBe("");
    expect(run.stdout.trim()).toBe("true false");
    expect(run.status).toBe(0);
  });
});
