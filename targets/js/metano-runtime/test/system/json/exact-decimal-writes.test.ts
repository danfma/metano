import { afterAll, describe, expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Decimal } from "decimal.js";
import { exactNumberWriterOf } from "#/system/json/exact-json-number";
import { JsonSerializer, type ValueSpec } from "#/system/json";
import {
  DECIMAL_WRITE_ROWS,
  decimalSpec,
  expectedTextOf,
  treeRoundTripOf,
  writtenTextOf,
} from "./exact-decimal-rows.ts";

const PROBE = join(import.meta.dir, "exact-decimal-probe.ts");

/** What every runtime must print: STJ's text, except for the scale. */
const EXPECTED_TEXTS = DECIMAL_WRITE_ROWS.map(expectedTextOf);

/** What an engine without `JSON.rawJSON` prints: the double, as before #458. */
const FALLBACK_TEXTS = DECIMAL_WRITE_ROWS.map((row) =>
  JSON.stringify(new Decimal(row.text).toNumber()),
);

describe("JsonSerializer — decimal writes match System.Text.Json (#458)", () => {
  for (const row of DECIMAL_WRITE_ROWS) {
    test(`${row.csharp} is written ${expectedTextOf(row)}`, () => {
      expect(writtenTextOf(row)).toBe(expectedTextOf(row));
    });

    test(`${row.csharp} round-trips through the tree`, () => {
      expect(treeRoundTripOf(row)?.equals(new Decimal(row.text))).toBe(true);
    });

    // JSON.parse reads the written text as a double, so the text round-trip
    // is exact only for values a double holds; exact reads are #457.
    test(`${row.csharp} round-trips through the text as JSON.parse reads it`, () => {
      const read = JsonSerializer.deserialize(JSON.parse(writtenTextOf(row)), decimalSpec);

      expect(read?.equals(new Decimal(Number(row.text)))).toBe(true);
    });
  }

  test("the scale is the remaining gap: trailing zeros are dropped", () => {
    const scaled = DECIMAL_WRITE_ROWS.filter((row) => row.metano !== undefined);

    expect(scaled.map((row) => [row.stj, writtenTextOf(row)])).toEqual([
      ["1.10", "1.1"],
      ["1.0", "1"],
      ["0.00", "0"],
      ["1000.000", "1000"],
      ["0.00", "0"],
    ]);
  });

  test("a dictionary value is written unquoted inside its object", () => {
    const spec: ValueSpec<Map<string, Decimal>> = {
      descriptor: { kind: "map", key: { kind: "primitive" }, value: decimalSpec.descriptor },
    };

    const tree = JsonSerializer.serialize(
      new Map([["max", new Decimal("79228162514264337593543950335")]]),
      spec,
    );

    expect(JSON.stringify(tree)).toBe('{"max":79228162514264337593543950335}');
  });

  test("null is written null", () => {
    expect(JsonSerializer.serialize(null, decimalSpec)).toBeNull();
  });
});

describe("JsonSerializer — decimal raw values without a descriptor type", () => {
  const rawSpec: ValueSpec<unknown> = { descriptor: { kind: "decimal" } };

  test("a raw JSON number is written back as is", () => {
    const read = JsonSerializer.deserialize(JSON.parse("19.99"), rawSpec);

    expect(JsonSerializer.serialize(read, rawSpec)).toBe(19.99);
  });

  test("a raw JSON string read from a string is written back as is", () => {
    const lenient: ValueSpec<unknown> = {
      ...rawSpec,
      readOptions: { numberHandling: "allowReadingFromString" },
    };

    const read = JsonSerializer.deserialize("1.10", lenient);

    expect(JsonSerializer.serialize(read, lenient)).toBe("1.10");
  });

  test("a raw JSON leaf of the tree is kept, so it is written back unchanged", () => {
    const leaf = JsonSerializer.serialize(new Decimal("1.5"), decimalSpec);

    const read = JsonSerializer.deserialize(leaf, rawSpec);

    expect(read).toBe(leaf);
    expect(JSON.stringify(JsonSerializer.serialize(read, rawSpec))).toBe("1.5");
  });
});

describe("JsonSerializer — decimal raw JSON leaves on read", () => {
  test("a leaf outside the decimal range throws the conversion error", () => {
    const rawJson = (JSON as unknown as { rawJSON(text: string): unknown }).rawJSON;

    expect(() => JsonSerializer.deserialize(rawJson("1e29"), decimalSpec)).toThrow(
      "The JSON value could not be converted to System.Decimal.",
    );
  });
});

describe("exactNumberWriterOf", () => {
  test("writes the raw JSON text when the engine has JSON.rawJSON", () => {
    const write = exactNumberWriterOf((text) => `raw:${text}`);

    expect(write("1.10", () => 1.1)).toBe("raw:1.10");
  });

  test("falls back to the approximate number without JSON.rawJSON", () => {
    const write = exactNumberWriterOf(undefined);

    expect(write("79228162514264337593543950335", () => 7.922816251426434e28)).toBe(
      7.922816251426434e28,
    );
  });
});

describe("decimal writes on each runtime", () => {
  const bundleDir = mkdtempSync(join(tmpdir(), "metano-decimal-probe-"));
  const bundle = join(bundleDir, "probe.mjs");

  const build = spawnSync(
    process.execPath,
    ["build", PROBE, "--target=node", `--outfile=${bundle}`],
    { encoding: "utf8" },
  );

  afterAll(() => rmSync(bundleDir, { recursive: true, force: true }));

  const textsOf = (command: string, args: readonly string[]) => {
    const run = spawnSync(command, args, { encoding: "utf8" });

    expect(run.stderr).toBe("");
    expect(run.status).toBe(0);

    return JSON.parse(run.stdout) as string[];
  };

  test("the probe bundles for Node", () => {
    expect(build.status).toBe(0);
  });

  test("Node writes the System.Text.Json digits", () => {
    expect(textsOf("node", [bundle])).toEqual(EXPECTED_TEXTS);
  });

  test("Node without JSON.rawJSON writes toNumber()", () => {
    expect(textsOf("node", [bundle, "--without-raw-json"])).toEqual(FALLBACK_TEXTS);
  });

  test("Bun writes the System.Text.Json digits", () => {
    expect(textsOf(process.execPath, [PROBE])).toEqual(EXPECTED_TEXTS);
  });

  test("Bun without JSON.rawJSON writes toNumber()", () => {
    expect(textsOf(process.execPath, [PROBE, "--without-raw-json"])).toEqual(FALLBACK_TEXTS);
  });
});
