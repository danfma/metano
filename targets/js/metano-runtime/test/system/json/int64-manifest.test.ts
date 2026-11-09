import { describe, expect, test } from "bun:test";
import {
  JsonSerializer,
  type PropertySpec,
  type TypeDescriptor,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";
import manifest from "../../../../sample-json-contracts/test/fixtures/stj/cases.json";

/**
 * The 64-bit manifest rows (`I*` in the shared STJ conformance fixture,
 * spec 012 slice 012e) run against hand-written `int64` specs mirroring the
 * sample's `Int64Box` / `Int64Containers` — the TypeScript runtime side of
 * rows whose generated contexts still carry the 2.x number descriptors
 * until the bigint flip (012k).
 *
 * Reads go through `deserializeText`, exactly as System.Text.Json reads its
 * text: the exact tree hands every 64-bit leaf that is not a short integer
 * token its source text (FR-053, slice 012h), so the accepted reads and the
 * errors match the STJ expectations of the manifest. (A parsed tree could
 * not: `JSON.parse` has already rounded a 19-digit integer or folded `1.0`
 * into `1`.) Writes re-serialize what the row's text read into and must
 * print the row's text byte for byte.
 *
 * One divergence remains by design: the nullable member errors of I6 and
 * I17 name ``System.Nullable`1[System.Int64]`` where STJ names the
 * declaring contract — the 009 nullable-inner design, primitives included.
 * (I5, `{"Id":1.0}`, read as `1n` until 012h: a text of at most 15
 * significant digits went through `JSON.parse`, which folds the fraction.)
 *
 * The mirrors mark the members a partial payload omits `optional`: the
 * runtime throws `Missing required property` for an absent required member
 * before any conversion error, which would hide the error the row pins
 * (STJ defaults missing members instead).
 */

interface ManifestRow {
  readonly id: string;
  readonly getter: string;
  readonly direction: string;
  readonly json?: string;
  readonly error?: { readonly class: string; readonly message: string };
}

/** The manifest's `I*` rows, in file order. */
const ROWS = (manifest as ManifestRow[]).filter((row) => /^I\d+$/.test(row.id));

const LONG: TypeDescriptor = { kind: "int64", numberType: "System.Int64" };

const ULONG: TypeDescriptor = { kind: "int64", numberType: "System.UInt64" };

class Int64Box {}

const int64Box: TypeSpec = {
  type: Int64Box,
  factory: (props) => Object.assign(new Int64Box(), props),
  properties: [
    { ts: "id", json: "Id", type: LONG, optional: true },
    { ts: "mask", json: "Mask", type: ULONG, optional: true },
    {
      ts: "parent",
      json: "Parent",
      type: { kind: "nullable", inner: LONG },
      optional: true,
    },
  ] as PropertySpec[],
};

class Int64Containers {}

const int64Containers: TypeSpec = {
  type: Int64Containers,
  factory: (props) => Object.assign(new Int64Containers(), props),
  properties: [
    { ts: "values", json: "Values", type: { kind: "array", element: LONG }, optional: true },
    {
      ts: "byName",
      json: "ByName",
      type: { kind: "map", key: LONG, value: { kind: "primitive" } },
      optional: true,
    },
    {
      ts: "byMask",
      json: "ByMask",
      type: { kind: "map", key: ULONG, value: { kind: "primitive" } },
      optional: true,
    },
  ] as PropertySpec[],
};

const longRoot: ValueSpec = { descriptor: LONG, valueType: true, typeName: "System.Int64" };

const ulongRoot: ValueSpec = { descriptor: ULONG, valueType: true, typeName: "System.UInt64" };

/** The spec each row reads through, named by its manifest getter. */
function specOf(getter: string): TypeSpec | ValueSpec {
  switch (getter) {
    case "int64Box":
      return int64Box;
    case "int64Containers":
      return int64Containers;
    case "int64":
      return longRoot;
    case "uInt64":
      return ulongRoot;
    default:
      throw new Error(`no int64 mirror spec for the manifest getter '${getter}'`);
  }
}

/**
 * The message the runtime throws for an error row: the manifest's STJ
 * expectation, except I6 and I17 (the nullable-inner name above).
 */
const RUNTIME_ERROR_OF: Readonly<Record<string, string>> = {
  I4: cannotConvertMessage("Int64Box"),
  I5: cannotConvertMessage("Int64Box"),
  I6: cannotConvertMessage("System.Nullable`1[System.Int64]"),
  I7: cannotConvertMessage("System.Int64"),
  I8: cannotConvertMessage("System.UInt64"),
  I9: cannotConvertMessage("System.Int64"),
  I10: cannotConvertMessage("System.UInt64"),
  I11: cannotConvertMessage("System.Int64"),
  I13: cannotConvertMessage("System.Int64"),
  I14: cannotConvertMessage("System.UInt64"),
  I15: cannotConvertMessage("System.Int64"),
  I16: cannotConvertMessage("Int64Box"),
  I17: cannotConvertMessage("System.Nullable`1[System.Int64]"),
  I18: cannotConvertMessage("System.Int64"),
};

type Int64BoxFields = { id: bigint; mask: bigint; parent: bigint | null };

/** The fields a read or roundtrip row must read back, by row id. */
const READ_ASSERTIONS_OF: Readonly<Record<string, (value: unknown) => void>> = {
  I2: (value) => {
    expect(value).toBeInstanceOf(Int64Box);

    const box = value as Int64BoxFields;

    expect(box.id).toBe(42n);
    expect(box.mask).toBe(7n);
    expect(box.parent).toBeNull();
  },
  I12: (value) => {
    expect(value).toBeInstanceOf(Int64Containers);

    const containers = value as { byName: Map<bigint, string> };

    expect(containers.byName.get(5n)).toBe("b");
    expect(containers.byName.get(0n)).toBe("c");
  },
  I19: (value) => {
    expect(value).toBeInstanceOf(Int64Box);

    const box = value as Int64BoxFields;

    expect(box.id).toBe(9007199254740993n);
    expect(box.mask).toBe(18446744073709551615n);
    expect(box.parent).toBe(-9223372036854775808n);
  },
  I20: (value) => expect(value).toBe(0n),
};

function assertReadOf(rowId: string): (value: unknown) => void {
  const assertRead = READ_ASSERTIONS_OF[rowId];

  if (assertRead === undefined) {
    throw new Error(`no read assertions for the manifest row '${rowId}'`);
  }

  return assertRead;
}

describe("int64 manifest rows — the runtime side of the STJ conformance fixture", () => {
  test("every I row carries the json text the mirrors need", () => {
    for (const row of ROWS) {
      expect([row.id, typeof row.json]).toEqual([row.id, "string"]);
    }
  });

  for (const row of ROWS) {
    test(`${row.id} ${row.direction} ${row.getter}`, () => {
      const spec = specOf(row.getter);
      const text = row.json as string;

      if (row.error !== undefined) {
        expectJsonException(
          () => JsonSerializer.deserializeText(text, spec),
          RUNTIME_ERROR_OF[row.id],
        );

        return;
      }

      const value = JsonSerializer.deserializeText(text, spec);

      if (row.direction !== "write") {
        assertReadOf(row.id)(value);
      }

      if (row.direction === "read") {
        return;
      }

      // A write or roundtrip row: what the row's STJ text read into prints
      // back as the same text.
      expect(JSON.stringify(JsonSerializer.serialize(value, spec))).toBe(text);
    });
  }
});
