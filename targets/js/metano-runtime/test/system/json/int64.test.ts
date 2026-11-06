import { describe, expect, test } from "bun:test";
import {
  type JsonConverter,
  JsonException,
  JsonSerializer,
  type JsonReadOptions,
  type NumberTypeName,
  type PropertySpec,
  type TypeDescriptor,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { readNumberText } from "#/system/json/number-from-string.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";

/**
 * The `int64` descriptor (spec 012 FR-050–FR-057): a `long` / `ulong` holds
 * exact digits on write (`JSON.rawJSON`, the #458 writer) and reads an
 * integer leaf — a `bigint`, a `rawJSON` leaf of a JSON integer token, a
 * safe-integer `number`, or a string in the numbers-from-strings grammar
 * when the context allows it — into a `bigint` in range. Every System.Text
 * .Json expectation below was measured on .NET 10 (scratch STJ probe,
 * 2026-11-06):
 *
 * - Writes print the exact digits, unquoted (`{"id":1152921504606846977}`),
 *   and dictionary keys are plain digit strings.
 * - Root reads: `-0` reads `0`; `1.0` and `1e3` throw; out of range throws
 *   the same conversion error; `null` throws for the value type and reads
 *   `null` through the nullable wrapper.
 * - Errors name the position: a root or an element names the 64-bit type
 *   (`System.Int64`, `System.UInt64`), a nullable root or element names
 *   ``System.Nullable`1[System.Int64]``, a property names the declaring
 *   contract, a dictionary key names the key type.
 * - Dictionary keys: `"+5"` and `"05"` read for `long`, `"05"` alone for
 *   `ulong` (`"+5"` and `"-0"` throw), `" 5"` and `"1e3"` throw for both.
 */

const READS_NUMBERS: JsonReadOptions = { numberHandling: "allowReadingFromString" };

const LONG_MIN = -9223372036854775808n;

const LONG_MAX = 9223372036854775807n;

const ULONG_MAX = 18446744073709551615n;

/** The `number` `JSON.parse("1152921504606846977")` gives, past the safe integers. */
const BEYOND_SAFE = JSON.parse("1152921504606846977") as number;

/** A `long` (or `ulong`) value root, with the name its errors report. */
function int64Root(numberType: NumberTypeName, readOptions?: JsonReadOptions): ValueSpec {
  return {
    descriptor: { kind: "int64", numberType },
    valueType: true,
    typeName: numberType,
    readOptions,
  };
}

/** Reads a tree value through a `long` / `ulong` root descriptor. */
function readRoot(data: unknown, numberType: NumberTypeName, readOptions?: JsonReadOptions): unknown {
  return JsonSerializer.deserialize(data, int64Root(numberType, readOptions));
}

/** The tree `serialize` wrote, stringified. */
function writtenText(value: unknown, numberType: NumberTypeName): string {
  return JSON.stringify(JsonSerializer.serialize(value, int64Root(numberType)));
}

describe("int64 — writes (FR-051)", () => {
  test("every 64-bit value writes its exact digits, unquoted", () => {
    expect(writtenText(1152921504606846977n, "System.Int64")).toBe("1152921504606846977");
    expect(writtenText(LONG_MIN, "System.Int64")).toBe("-9223372036854775808");
    expect(writtenText(LONG_MAX, "System.Int64")).toBe("9223372036854775807");
    expect(writtenText(ULONG_MAX, "System.UInt64")).toBe("18446744073709551615");
    expect(writtenText(0n, "System.Int64")).toBe("0");
    expect(writtenText(-0n, "System.Int64")).toBe("0");
  });

  test("the write is a rawJSON leaf, so a tree round-trips without text (FR-057)", () => {
    const leaf = JsonSerializer.serialize(1152921504606846977n, int64Root("System.Int64"));

    expect(JSON.isRawJSON(leaf)).toBe(true);
    expect(readRoot(leaf, "System.Int64")).toBe(1152921504606846977n);
  });

  test("null writes null, and a safe-integer number writes its digits", () => {
    expect(writtenText(null, "System.Int64")).toBe("null");
    expect(writtenText(42, "System.Int64")).toBe("42");
  });

  test("a number beyond the safe integers, a fraction and any other kind throw", () => {
    expect(Number.isSafeInteger(BEYOND_SAFE)).toBe(false);

    for (const value of [BEYOND_SAFE, 1.5, Number.NaN, Number.POSITIVE_INFINITY, "5", true, {}]) {
      expect(() => writtenText(value, "System.Int64")).toThrow(JsonException);
    }
  });

  test("a nullable slot writes null for null and its digits for the value", () => {
    const nullable: TypeDescriptor = {
      kind: "nullable",
      inner: { kind: "int64", numberType: "System.Int64" },
    };

    expect(JSON.stringify(JsonSerializer.serialize(null, { descriptor: nullable }))).toBe("null");
    expect(JSON.stringify(JsonSerializer.serialize(LONG_MIN, { descriptor: nullable }))).toBe(
      "-9223372036854775808",
    );
  });

  test("a custom converter of the kind still replaces the built-in one", () => {
    const custom: JsonConverter[] = [
      { kind: "int64", serialize: (v) => `n:${String(v)}`, deserialize: () => 1n },
    ];

    expect(JsonSerializer.serialize(5n, int64Root("System.Int64"), custom)).toBe("n:5");
    expect(JsonSerializer.deserialize("0", int64Root("System.Int64"), custom)).toBe(1n);
  });
});

describe("int64 — tree reads (FR-052)", () => {
  test("a bigint in range reads itself; out of range throws", () => {
    expect(readRoot(5n, "System.Int64")).toBe(5n);
    expect(readRoot(LONG_MIN, "System.Int64")).toBe(LONG_MIN);
    expect(readRoot(ULONG_MAX, "System.UInt64")).toBe(ULONG_MAX);

    expectJsonException(
      () => readRoot(ULONG_MAX, "System.Int64"),
      cannotConvertMessage("System.Int64"),
    );
    expectJsonException(() => readRoot(-1n, "System.UInt64"), cannotConvertMessage("System.UInt64"));
    expectJsonException(
      () => readRoot(2n ** 64n, "System.UInt64"),
      cannotConvertMessage("System.UInt64"),
    );
  });

  test("a rawJSON leaf of a JSON integer token reads exactly", () => {
    expect(readRoot(JSON.rawJSON("1152921504606846977"), "System.Int64")).toBe(
      1152921504606846977n,
    );
    expect(readRoot(JSON.rawJSON("-9223372036854775808"), "System.Int64")).toBe(LONG_MIN);
    expect(readRoot(JSON.rawJSON("18446744073709551615"), "System.UInt64")).toBe(ULONG_MAX);
  });

  test("-0 reads 0, and 1.0 / 1e3 are rejected (measured: STJ reads 0 and throws)", () => {
    expect(readRoot(JSON.rawJSON("-0"), "System.Int64")).toBe(0n);
    expect(readRoot(JSON.rawJSON("0"), "System.UInt64")).toBe(0n);

    // `JSON.rawJSON` builds a leaf only of valid JSON text, so the signed
    // spellings `"+5"` and `"05"` cannot be leaves; they are the string
    // grammar's cases (FR-054 below).
    for (const token of ["1.0", "1e3", "9223372036854775808", "-9223372036854775809"]) {
      expectJsonException(
        () => readRoot(JSON.rawJSON(token), "System.Int64"),
        cannotConvertMessage("System.Int64"),
      );
    }

    for (const token of ["-0", "1.0", "1e3", "18446744073709551616"]) {
      expectJsonException(
        () => readRoot(JSON.rawJSON(token), "System.UInt64"),
        cannotConvertMessage("System.UInt64"),
      );
    }
  });

  test("a safe-integer number reads BigInt(n)", () => {
    expect(readRoot(5, "System.Int64")).toBe(5n);
    expect(readRoot(-3, "System.Int64")).toBe(-3n);
    expect(readRoot(0, "System.UInt64")).toBe(0n);
  });

  test("a number beyond the safe integers names the exact read path (OQ-3 d)", () => {
    expectJsonException(
      () => readRoot(BEYOND_SAFE, "System.Int64"),
      "The JSON value 1152921504606846976 could not be converted to System.Int64: it is beyond "
        + "the integers a JS number holds exactly. Read the JSON text with "
        + "JsonSerializer.deserializeText, which keeps every digit.",
    );
  });

  test("a fractional number, NaN and Infinity throw the conversion error", () => {
    for (const value of [1.5, Number.NaN, Number.POSITIVE_INFINITY, -Number.POSITIVE_INFINITY]) {
      expectJsonException(
        () => readRoot(value, "System.Int64"),
        cannotConvertMessage("System.Int64"),
      );
    }
  });

  test("a string is rejected without the option, and read by the string grammar with it (FR-054)", () => {
    expectJsonException(() => readRoot("5", "System.Int64"), cannotConvertMessage("System.Int64"));
    expect(readRoot("5", "System.Int64", READS_NUMBERS)).toBe(5n);
  });

  test("null throws for the value-type root and reads null through the nullable wrapper", () => {
    expectJsonException(() => readRoot(null, "System.Int64"), cannotConvertMessage("System.Int64"));

    const nullable: TypeDescriptor = {
      kind: "nullable",
      inner: { kind: "int64", numberType: "System.Int64" },
    };

    expect(JsonSerializer.deserialize(null, { descriptor: nullable })).toBeNull();

    expectJsonException(
      () => JsonSerializer.deserialize("x", { descriptor: nullable, readOptions: READS_NUMBERS }),
      cannotConvertMessage("System.Nullable`1[System.Int64]"),
    );
  });

  test("an element reads its values and names the element type in its errors", () => {
    const array: TypeDescriptor = {
      kind: "array",
      element: { kind: "int64", numberType: "System.Int64" },
    };

    expect(JsonSerializer.deserialize([1, 2], { descriptor: array })).toEqual([1n, 2n]);

    expectJsonException(
      () => JsonSerializer.deserialize([1, "x"], { descriptor: array }),
      cannotConvertMessage("System.Int64"),
    );
    expectJsonException(
      () => JsonSerializer.deserialize([1, null], { descriptor: array }),
      cannotConvertMessage("System.Int64"),
    );
  });

  test("a property error names the declaring contract, as System.Text.Json does (#485)", () => {
    class Snowflake {}

    const spec: TypeSpec = {
      type: Snowflake,
      factory: () => new Snowflake(),
      properties: [
        {
          ts: "id",
          json: "Id",
          type: { kind: "int64", numberType: "System.Int64" },
        } as PropertySpec,
      ],
    };

    expectJsonException(
      () => JsonSerializer.deserialize({ Id: "x" }, spec),
      cannotConvertMessage("Snowflake"),
    );
  });

  test("an object slot keeps today's read: the JSON value as parsed (FR-056)", () => {
    expect(JsonSerializer.deserialize(5, { descriptor: { kind: "primitive" } })).toBe(5);
    expect(JsonSerializer.deserialize({ id: 5 }, { descriptor: { kind: "primitive" } })).toEqual({
      id: 5,
    });
  });
});

describe("int64 — numbers from strings (FR-054)", () => {
  test("readNumberText answers the bigint it computes for the 64-bit types", () => {
    expect(readNumberText("9223372036854775807", "System.Int64")).toBe(LONG_MAX);
    expect(readNumberText("-9223372036854775808", "System.Int64")).toBe(LONG_MIN);
    expect(readNumberText("18446744073709551615", "System.UInt64")).toBe(ULONG_MAX);
    expect(readNumberText("9007199254740993", "System.Int64")).toBe(9007199254740993n);
    expect(readNumberText("05", "System.UInt64")).toBe(5n);
    expect(readNumberText("5", "System.Int32")).toBe(5);
  });

  test("the 64-bit string grammar is the FR-048 one, signed and unsigned", () => {
    expect(readRoot("+5", "System.Int64", READS_NUMBERS)).toBe(5n);
    expect(readRoot("05", "System.Int64", READS_NUMBERS)).toBe(5n);
    expect(readRoot("-0", "System.Int64", READS_NUMBERS)).toBe(0n);
    expect(readRoot("9223372036854775807", "System.Int64", READS_NUMBERS)).toBe(LONG_MAX);
    expect(readRoot("18446744073709551615", "System.UInt64", READS_NUMBERS)).toBe(ULONG_MAX);

    const rejected: readonly [text: string, numberType: NumberTypeName][] = [
      ["1e3", "System.Int64"],
      ["5.0", "System.Int64"],
      [" 5", "System.Int64"],
      ["9223372036854775808", "System.Int64"],
      ["+5", "System.UInt64"],
      ["-0", "System.UInt64"],
      [" 5", "System.UInt64"],
      ["1e3", "System.UInt64"],
      ["18446744073709551616", "System.UInt64"],
    ];

    for (const [text, numberType] of rejected) {
      expectJsonException(
        () => readRoot(text, numberType, READS_NUMBERS),
        cannotConvertMessage(numberType),
      );
    }
  });

  test("a primitive slot keeps its number: the reader's bigint is read as a number there", () => {
    const primitive: ValueSpec = {
      descriptor: { kind: "primitive", numberType: "System.Int64" },
      readOptions: READS_NUMBERS,
    };

    expect(JsonSerializer.deserialize("5", primitive)).toBe(5);
    expect(JsonSerializer.deserialize("9007199254740993", primitive)).toBe(
      JSON.parse("9007199254740993"),
    );
  });
});

describe("int64 — dictionary keys (FR-055)", () => {
  const longKeys: TypeDescriptor = {
    kind: "map",
    key: { kind: "int64", numberType: "System.Int64" },
    value: { kind: "primitive" },
  };

  const ulongKeys: TypeDescriptor = {
    kind: "map",
    key: { kind: "int64", numberType: "System.UInt64" },
    value: { kind: "primitive" },
  };

  test("keys write key.toString()", () => {
    const tree = JsonSerializer.serialize(
      new Map<bigint, string>([
        [LONG_MAX, "a"],
        [-3n, "b"],
      ]),
      { descriptor: longKeys },
    );

    expect(JSON.stringify(tree)).toBe('{"9223372036854775807":"a","-3":"b"}');
    expect(JSON.stringify(JsonSerializer.serialize(new Map([[ULONG_MAX, "a"]]), { descriptor: ulongKeys }))).toBe(
      '{"18446744073709551615":"a"}',
    );
  });

  test("keys read the FR-048 integer grammar, whatever the read options (measured)", () => {
    expect(
      JsonSerializer.deserialize({ "05": "a", "+7": "b", "-0": "c" }, { descriptor: longKeys }),
    ).toEqual(
      new Map<bigint, string>([
        [5n, "a"],
        [7n, "b"],
        [0n, "c"],
      ]),
    );
    expect(JsonSerializer.deserialize({ "05": "a" }, { descriptor: ulongKeys })).toEqual(
      new Map<bigint, string>([[5n, "a"]]),
    );

    const withOption: TypeDescriptor = { ...longKeys, readOptions: READS_NUMBERS };

    expect(JsonSerializer.deserialize({ "05": "a", "+7": "b" }, { descriptor: withOption })).toEqual(
      new Map<bigint, string>([
        [5n, "a"],
        [7n, "b"],
      ]),
    );
  });

  test("a rejected key throws the conversion error naming the key type (measured)", () => {
    const rejectedLong = [" 5", "1e3", "5.0", "9223372036854775808"];
    const rejectedUlong = ["-0", "+5", " 5", "1e3", "18446744073709551616"];

    for (const name of rejectedLong) {
      expectJsonException(
        () => JsonSerializer.deserialize({ [name]: "a" }, { descriptor: longKeys }),
        cannotConvertMessage("System.Int64"),
      );
    }

    for (const name of rejectedUlong) {
      expectJsonException(
        () => JsonSerializer.deserialize({ [name]: "a" }, { descriptor: ulongKeys }),
        cannotConvertMessage("System.UInt64"),
      );
    }
  });
});

describe("int64 — object slots (FR-056)", () => {
  test("a bigint through a passthrough descriptor writes an exact number leaf", () => {
    const passthrough: TypeDescriptor = { kind: "primitive" };

    const leaf = JsonSerializer.serialize(1152921504606846977n, { descriptor: passthrough });

    expect(JSON.isRawJSON(leaf)).toBe(true);
    expect(JSON.stringify(leaf)).toBe("1152921504606846977");
    expect(JSON.stringify(JsonSerializer.serialize(5n, { descriptor: passthrough }))).toBe("5");
  });

  test("a bigint in a plain-object slot of a contract writes exactly", () => {
    const spec: TypeSpec<{ value: bigint }> = {
      factory: (props) => props as { value: bigint },
      properties: [
        { ts: "value", json: "Value", type: { kind: "primitive" } } as PropertySpec,
      ],
    };

    const tree = JsonSerializer.serialize({ value: 1152921504606846977n }, spec);

    expect(JSON.stringify(tree)).toBe('{"Value":1152921504606846977}');
  });

  test("a bigint never reaches JSON.stringify through a container", () => {
    const array: TypeDescriptor = { kind: "array", element: { kind: "primitive" } };

    const tree = JsonSerializer.serialize([1152921504606846977n], { descriptor: array });

    expect(JSON.stringify(tree)).toBe("[1152921504606846977]");
  });
});
