import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Decimal } from "decimal.js";
import {
  JsonException,
  JsonSerializer,
  SerializerContext,
  type JsonConverter,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json";
import { specNeedsExactNumbers } from "#/system/json/exact-number-reads";
import {
  DECIMAL_READ_ROWS,
  decimalSpec,
  expectedReadOf,
  readOutcomeOf,
  STJ_DECIMAL_TEXTS,
  textRoundTripOf,
  withoutScale,
} from "./exact-decimal-read-rows.ts";
import { DECIMAL_WRITE_ROWS, expectedTextOf, writtenTextOf } from "./exact-decimal-rows.ts";
import { expectedTextOutcomeOf, JSON_TEXT_ROWS } from "./json-text-rows.ts";

const PROBE = join(import.meta.dir, "exact-decimal-read-probe.ts");

/** A long number: more significant digits than a double holds, so the text is tokenized. */
const LONG = "12345678901234567890.123456789";

const nestedArrays = (depth: number) => `${"[".repeat(depth)}${"]".repeat(depth)}`;

/** `depth` nested arrays of numbers, as a descriptor. */
function nestedListOf(depth: number): TypeSpec["properties"][number]["type"] {
  return depth === 0 ? { kind: "primitive" } : { kind: "array", element: nestedListOf(depth - 1) };
}

type Holder = Record<string, unknown>;

const primitive = { kind: "primitive" } as const;
const decimal = { kind: "decimal", type: Decimal } as const;

function holderSpec(properties: TypeSpec["properties"]): TypeSpec<Holder> {
  return { factory: (values) => values, properties };
}

describe("deserializeText — decimals as System.Text.Json reads them (#457)", () => {
  for (const row of DECIMAL_READ_ROWS) {
    const options = row.web ? " (Web)" : "";

    test(`${row.json}${options} → ${expectedReadOf(row)}`, () => {
      expect(readOutcomeOf(row)).toBe(expectedReadOf(row));
    });
  }

  test("STJ's texts of the #458 probe round-trip, except for the scale", () => {
    const mismatches = STJ_DECIMAL_TEXTS.filter(
      (text) => textRoundTripOf(text) !== withoutScale(text),
    );

    expect(mismatches).toEqual([]);
  });

  test("every text #458 writes reads back to the same digits", () => {
    for (const row of DECIMAL_WRITE_ROWS) {
      expect(textRoundTripOf(writtenTextOf(row))).toBe(expectedTextOf(row));
    }
  });

  test("the tree path keeps rounding long decimals to a double", () => {
    const read = JsonSerializer.deserialize(JSON.parse(LONG), decimalSpec);

    expect(read?.toFixed()).toBe("12345678901234567000");
  });

  test("a nullable decimal reads its exact digits", () => {
    const spec: ValueSpec<Decimal | null> = { descriptor: { kind: "nullable", inner: decimal } };

    expect(JsonSerializer.deserializeText(LONG, spec)?.toFixed()).toBe(LONG);
    expect(JsonSerializer.deserializeText("null", spec)).toBeNull();
  });

  test("a decimal without a descriptor type reads the number JSON.parse gives", () => {
    const raw: ValueSpec<unknown> = { descriptor: { kind: "decimal" } };

    expect(
      JsonSerializer.deserializeText(`[${LONG}]`, {
        descriptor: { kind: "array", element: raw.descriptor },
      }),
    ).toEqual([Number(LONG)]);
  });
});

describe("deserializeText — the other values of an exact tree read as JSON.parse reads them", () => {
  const spec = holderSpec([
    { ts: "price", json: "price", type: decimal },
    { ts: "ratio", json: "ratio", type: primitive },
    { ts: "id", json: "id", type: { kind: "nullable", inner: primitive } },
    { ts: "list", json: "list", type: { kind: "array", element: primitive } },
  ]);

  test("doubles and integers beyond 2^53 are numbers", () => {
    const read = JsonSerializer.deserializeText(
      `{"price":${LONG},"ratio":0.30000000000000004,"id":12345678901234567890,"list":[1.00000000000000001]}`,
      spec,
    );

    expect(read).toEqual({
      price: new Decimal(LONG),
      ratio: 0.30000000000000004,
      id: 12345678901234567000,
      list: [1],
    });
  });

  test("a long number where a container belongs throws the number's error", () => {
    const text = `{"price":1,"ratio":1,"id":1,"list":${LONG}}`;

    expect(() => JsonSerializer.deserializeText(text, spec)).toThrow(
      "Expected array for deserialization, got number",
    );
  });

  test("a long number where a contract belongs cannot be converted to it", () => {
    const nested = holderSpec([
      { ts: "inner", json: "inner", type: { kind: "ref", spec: () => spec } },
    ]);

    expect(() => JsonSerializer.deserializeText(`{"inner":${LONG}}`, nested)).toThrow(
      JsonException,
    );
    expect(() => JsonSerializer.deserializeText(LONG, spec)).toThrow(JsonException);
  });

  test("a dictionary value that is a long number is not read as an object", () => {
    const dictionary: ValueSpec<Map<string, unknown>> = {
      descriptor: {
        kind: "map",
        key: primitive,
        value: { kind: "map", key: primitive, value: decimal },
      },
    };

    expect(() => JsonSerializer.deserializeText(`{"a":${LONG}}`, dictionary)).toThrow(
      "Expected object for deserialization, got number",
    );
  });

  test("a custom converter gets the number JSON.parse gives", () => {
    const seen: unknown[] = [];

    const primitives: JsonConverter = {
      kind: "primitive",
      serialize: (value) => value,
      deserialize: (value) => {
        seen.push(value);
        return value;
      },
    };

    JsonSerializer.deserializeText(
      `{"price":${LONG},"ratio":0.30000000000000004,"id":null,"list":[]}`,
      spec,
      [primitives],
    );

    expect(seen).toEqual([0.30000000000000004]);
  });

  // A custom converter matches every descriptor of its kind and reads the
  // subtree itself, so none of its leaves may reach it.
  describe("a custom container converter gets no JsonNumberText anywhere in its subtree", () => {
    const LONG_INTEGER = "12345678901234567890";
    const inner = holderSpec([{ ts: "n", json: "n", type: primitive }]);

    const cases: readonly [
      JsonConverter["kind"],
      TypeSpec["properties"][number]["type"],
      string,
    ][] = [
      ["array", { kind: "array", element: primitive }, `[[${LONG_INTEGER}]]`],
      ["hashSet", { kind: "hashSet", element: primitive }, `[${LONG_INTEGER}]`],
      ["map", { kind: "map", key: primitive, value: primitive }, `{"a":{"b":${LONG_INTEGER}}}`],
      ["ref", { kind: "ref", spec: () => inner }, `{"n":[${LONG_INTEGER}]}`],
      ["nullable", { kind: "nullable", inner: primitive }, `{"deep":[${LONG_INTEGER}]}`],
    ];

    for (const [kind, type, json] of cases) {
      test(kind, () => {
        const seen: unknown[] = [];

        const spy: JsonConverter = {
          kind,
          serialize: (value) => value,
          deserialize: (value) => {
            seen.push(value);
            return value;
          },
        };

        const spec = holderSpec([
          { ts: "price", json: "price", type: decimal },
          { ts: "value", json: "value", type },
        ]);

        JsonSerializer.deserializeText(`{"price":${LONG},"value":${json}}`, spec, [spy]);

        expect(seen).toEqual([JSON.parse(json)]);
      });
    }
  });

  test("a long number as a polymorphic tag cannot be converted to the base", () => {
    const base: TypeSpec<Holder> = {
      ...holderSpec([{ ts: "price", json: "price", type: decimal }]),
      polymorphism: {
        discriminator: "$type",
        derived: [{ tag: 1, spec: () => base as TypeSpec }],
        typeName: "Base",
      },
    };

    expect(() =>
      JsonSerializer.deserializeText(`{"$type":12345678901234567890,"price":1}`, base),
    ).toThrow("The JSON value could not be converted to Base.");
  });
});

describe("deserializeText — the path chosen per spec", () => {
  const withoutDecimals = holderSpec([
    { ts: "list", json: "list", type: { kind: "array", element: primitive } },
  ]);
  const withDecimals = holderSpec([
    { ts: "list", json: "list", type: { kind: "array", element: primitive } },
    { ts: "price", json: "price", type: { kind: "nullable", inner: decimal }, optional: true },
  ]);

  test("invalid text throws STJ's JsonException on every path", () => {
    const message = "'}' is an invalid start of a value.";

    expect(() => JsonSerializer.deserializeText('{"list":}', withoutDecimals)).toThrow(message);
    expect(() => JsonSerializer.deserializeText('{"list":}', withDecimals)).toThrow(message);
    expect(() => JsonSerializer.deserializeText(`{"price":${LONG},"list":}`, withDecimals)).toThrow(
      message,
    );
  });

  // FR-016's limit counts the containers a contract reads, whichever path
  // built the tree, so a digit run in a string changes nothing.
  test("an unread member nested past 64 levels reads on every path", () => {
    const deep = `{"list":[],"unknown":${nestedArrays(65)}`;

    expect(JsonSerializer.deserializeText(`${deep}}`, withoutDecimals)).toEqual({ list: [] });
    expect(JsonSerializer.deserializeText(`${deep}}`, withDecimals)).toEqual({
      list: [],
      price: null,
    });
    expect(JsonSerializer.deserializeText(`${deep},"price":${LONG}}`, withDecimals)).toEqual({
      list: [],
      price: new Decimal(LONG),
    });
    expect(
      JsonSerializer.deserializeText(`${deep},"note":"card 4111111111111111"}`, withDecimals),
    ).toEqual({ list: [], price: null });
  });

  test("a read container nested past 64 levels throws on every path", () => {
    const listOf = (depth: number) => `{"list":${nestedArrays(depth)}`;
    const message =
      "The maximum configured depth of 64 has been exceeded. Cannot read next JSON array.";
    const nested = holderSpec([{ ts: "list", json: "list", type: nestedListOf(64) }]);
    const nestedWithDecimals = holderSpec([
      { ts: "list", json: "list", type: nestedListOf(64) },
      { ts: "price", json: "price", type: decimal },
    ]);

    expect(() => JsonSerializer.deserializeText(`${listOf(65)}}`, nested)).toThrow(message);
    expect(() =>
      JsonSerializer.deserializeText(`${listOf(65)},"price":${LONG}}`, nestedWithDecimals),
    ).toThrow(message);
  });

  test("a bound serializer reads text with its context's converters", () => {
    const doubled: JsonConverter = {
      kind: "primitive",
      serialize: (value) => value,
      deserialize: (value) => (value as number) * 2,
    };

    class DoublingContext extends SerializerContext {
      constructor() {
        super({ converters: [doubled] });
      }
    }

    const serializer = JsonSerializer.withContext(new DoublingContext());

    expect(serializer.deserializeText('{"list":[1,2]}', withoutDecimals)).toEqual({ list: [2, 4] });
  });
});

describe("specNeedsExactNumbers — the closure of a spec", () => {
  test("a spec reads number texts through properties, bases, derived contracts and collections", () => {
    const leaf = holderSpec([{ ts: "price", json: "price", type: decimal }]);
    const derived: TypeSpec<Holder> = { ...holderSpec([]), base: leaf };

    const polymorphic: TypeSpec<Holder> = {
      ...holderSpec([]),
      polymorphism: {
        discriminator: "$type",
        derived: [{ tag: "d", spec: () => derived as TypeSpec }],
      },
    };

    const viaMap: ValueSpec<unknown> = {
      descriptor: { kind: "map", key: primitive, value: { kind: "ref", spec: () => polymorphic } },
    };

    expect(specNeedsExactNumbers(leaf)).toBe(true);
    expect(specNeedsExactNumbers(derived)).toBe(true);
    expect(specNeedsExactNumbers(polymorphic)).toBe(true);
    expect(specNeedsExactNumbers(viaMap)).toBe(true);
  });

  test("a recursive spec without decimals does not", () => {
    const tree: TypeSpec<Holder> = holderSpec([
      { ts: "name", json: "name", type: primitive },
      {
        ts: "children",
        json: "children",
        type: { kind: "array", element: { kind: "ref", spec: () => tree } },
      },
    ]);

    expect(specNeedsExactNumbers(tree)).toBe(false);
  });
});

describe("exact reads on each runtime", () => {
  type ProbeOutput = {
    readonly reads: string[];
    readonly texts: string[];
    readonly tokenized: string[];
    readonly parsed: string[];
  };

  const EXPECTED: ProbeOutput = {
    reads: DECIMAL_READ_ROWS.map(expectedReadOf),
    texts: STJ_DECIMAL_TEXTS.map(withoutScale),
    tokenized: JSON_TEXT_ROWS.map(expectedTextOutcomeOf),
    parsed: JSON_TEXT_ROWS.map(expectedTextOutcomeOf),
  };

  let bundleDir = "";
  let bundle = "";
  let buildStatus: number | null = null;

  beforeAll(() => {
    bundleDir = mkdtempSync(join(tmpdir(), "metano-decimal-read-probe-"));
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

  test("Node reads the System.Text.Json digits and errors", () => {
    expect(probeOf("node", [bundle])).toEqual(EXPECTED);
  });

  test("Bun reads the System.Text.Json digits and errors", () => {
    expect(probeOf(process.execPath, [PROBE])).toEqual(EXPECTED);
  });
});
