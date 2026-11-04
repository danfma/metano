import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Decimal } from "decimal.js";
import { exactNumberWriterOf } from "#/system/json/exact-json-number";
import { JsonSerializer, type ValueSpec } from "#/system/json";
import {
  DECIMAL_DOMAIN_ROWS,
  DECIMAL_WRITE_ROWS,
  decimalSpec,
  domainErrorOf,
  domainOutcomeOf,
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

/** Each domain row's outcome: the error, or the exact text. */
const EXPECTED_DOMAIN = DECIMAL_DOMAIN_ROWS.map((row) => row.written ?? domainErrorOf(row));

/** The same without `JSON.rawJSON`: the error, `0` for a negligible value, or the double. */
const FALLBACK_DOMAIN = DECIMAL_DOMAIN_ROWS.map((row) => {
  if (row.written === undefined) return domainErrorOf(row);

  return row.written === "0" ? "0" : JSON.stringify(new Decimal(row.text).toNumber());
});

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
    const withoutTrailingZeros = (text: string) => text.replace(/\.?0+$/, "");

    expect(scaled.map(writtenTextOf)).toEqual(scaled.map((row) => withoutTrailingZeros(row.stj)));
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

describe("JsonSerializer — decimal writes within the System.Decimal domain", () => {
  DECIMAL_DOMAIN_ROWS.forEach((row, index) => {
    test(`${row.text} gives ${EXPECTED_DOMAIN[index]}`, () => {
      expect(domainOutcomeOf(row)).toBe(EXPECTED_DOMAIN[index]!);
    });
  });

  // A decimal-like value without decimal.js's exponent is placed by its double.
  test("a value without an exponent is negligible below 1e-29 too", () => {
    const decimalLikeOf = (text: string) =>
      ({
        toNumber: () => Number(text),
        toFixed: () => new Decimal(text).toFixed(),
      }) as unknown as Decimal;

    const writtenOf = (text: string) =>
      JSON.stringify(JsonSerializer.serialize(decimalLikeOf(text), decimalSpec));

    expect(writtenOf("9.99e-29")).toBe("0.0000000000000000000000000000999");
    expect(writtenOf("9.99e-30")).toBe("0");
    expect(writtenOf("-1e-400")).toBe("0");

    const withoutToFixed = { toNumber: () => 9.99e-30 } as unknown as Decimal;

    expect(JsonSerializer.serialize(withoutToFixed, decimalSpec)).toBe(0);
  });

  test("a value without toFixed is checked too", () => {
    const notANumber = { toNumber: () => Number.NaN, toString: () => "NaN" };

    expect(() => JsonSerializer.serialize(notANumber as unknown as Decimal, decimalSpec)).toThrow(
      "The value NaN cannot be written as System.Decimal: it is outside the System.Decimal range.",
    );
  });

  test("1e400000000 is rejected before its digits are printed", () => {
    const start = performance.now();

    expect(() => JsonSerializer.serialize(new Decimal("1e400000000"), decimalSpec)).toThrow();
    expect(performance.now() - start).toBeLessThan(100);
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
  type ProbeOutput = { readonly written: string[]; readonly domain: string[] };

  let bundleDir = "";
  let bundle = "";
  let buildStatus: number | null = null;

  beforeAll(() => {
    bundleDir = mkdtempSync(join(tmpdir(), "metano-decimal-probe-"));
    bundle = join(bundleDir, "probe.mjs");

    buildStatus = spawnSync(
      process.execPath,
      ["build", PROBE, "--target=node", `--outfile=${bundle}`],
      { encoding: "utf8" },
    ).status;
  });

  afterAll(() => rmSync(bundleDir, { recursive: true, force: true }));

  const probeOf = (command: string, args: readonly string[]): ProbeOutput => {
    const run = spawnSync(command, args, { encoding: "utf8" });

    if (run.status !== 0) {
      throw new Error(`${command} ${args.join(" ")} exited ${run.status}: ${run.stderr}`);
    }

    return JSON.parse(run.stdout) as ProbeOutput;
  };

  test("the probe bundles for Node", () => {
    expect(buildStatus).toBe(0);
  });

  test("Node writes the System.Text.Json digits and checks the domain", () => {
    expect(probeOf("node", [bundle])).toEqual({ written: EXPECTED_TEXTS, domain: EXPECTED_DOMAIN });
  });

  test("Node without JSON.rawJSON writes toNumber() and checks the domain", () => {
    expect(probeOf("node", [bundle, "--without-raw-json"])).toEqual({
      written: FALLBACK_TEXTS,
      domain: FALLBACK_DOMAIN,
    });
  });

  test("Bun writes the System.Text.Json digits and checks the domain", () => {
    expect(probeOf(process.execPath, [PROBE])).toEqual({
      written: EXPECTED_TEXTS,
      domain: EXPECTED_DOMAIN,
    });
  });

  test("Bun without JSON.rawJSON writes toNumber() and checks the domain", () => {
    expect(probeOf(process.execPath, [PROBE, "--without-raw-json"])).toEqual({
      written: FALLBACK_TEXTS,
      domain: FALLBACK_DOMAIN,
    });
  });
});
