/**
 * The self-contained enum goldens of spec 011 FR-044 type-check under
 * `tsgo --strict`, and a `[Flags]` brand rejects a value of another enum.
 * The golden bundle check compiles goldens without type-checking, so this is
 * where the emitted enum shapes are proven to compile.
 */
import { expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { copyFileSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { packageBinary } from "../tree-shaking/harness.ts";

const RUNTIME_DIR = resolve(import.meta.dir, "../..");
const EXPECTED_DIR = resolve(RUNTIME_DIR, "../../../tests/Metano.Tests/Expected");

const GOLDENS = [
  "numeric-enum.ts",
  "numeric-enum-const.ts",
  "numeric-enum-flags.ts",
  "numeric-enum-int64.ts",
];

const CROSS_ENUM_FIXTURE = `import { Perms } from "./numeric-enum-flags.ts";
import { Color } from "./numeric-enum-const.ts";

export const SideA = { Up: 1 } as const;
export type SideA = number & { readonly __enum?: "SideA" };

declare const side: SideA;

// @ts-expect-error a [Flags] enum rejects a value of another branded enum
export const fromOtherFlags: Perms = side;

// @ts-expect-error a plain enum rejects an arbitrary number
export const fromNumber: Color = 5 as number;

export const combined: Perms = Perms.Read | Perms.Write;
export const fromCast: Perms = 3 as Perms;

// A 64-bit [Flags] enum's brand is bigint-based (#701).
import { Wide, Huge } from "./numeric-enum-int64.ts";

declare const perms: Perms;

// @ts-expect-error a 64-bit [Flags] enum rejects a number-based enum's value
export const wideFromPerms: Wide = perms;

// @ts-expect-error a 64-bit plain enum rejects an arbitrary bigint
export const hugeFromBigInt: Huge = 5n as bigint;

export const wideCombined: Wide = Wide.Low | Wide.High;
export const wideFromBigInt: Wide = 3n;
`;

test("the enum goldens type-check under tsgo --strict", () => {
  const directory = realpathSync(mkdtempSync(join(tmpdir(), "metano-strict-goldens-")));

  try {
    for (const golden of GOLDENS) {
      copyFileSync(join(EXPECTED_DIR, golden), join(directory, golden));
    }

    writeFileSync(join(directory, "cross-enum.ts"), CROSS_ENUM_FIXTURE);

    const run = spawnSync(
      packageBinary("tsgo"),
      [
        "--ignoreConfig",
        "--strict",
        "--noEmit",
        "--allowImportingTsExtensions",
        "--target",
        "es2022",
        "--module",
        "esnext",
        "--moduleResolution",
        "bundler",
        "--lib",
        "es2022",
        ...GOLDENS.map((golden) => join(directory, golden)),
        join(directory, "cross-enum.ts"),
      ],
      { cwd: RUNTIME_DIR, encoding: "utf8" },
    );

    expect(run.stdout + run.stderr).toBe("");
    expect(run.status).toBe(0);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}, 120_000);
