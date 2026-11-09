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
import { specNumberTextLeaves } from "#/system/json/exact-number-reads.ts";
import { readExactNumberText } from "#/system/json/number-from-string.ts";
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
 *   and dictionary keys are plain digit strings. A write range-checks: only
 *   an exact integer within the 64-bit range can be written.
 * - Root reads: `-0` reads `0`; `1.0` and `1e3` throw; out of range throws
 *   the same conversion error; `null` throws for the value type and reads
 *   `null` through the nullable wrapper.
 * - `deserializeText` reads every number that is not a short integer token
 *   from its exact text (FR-053, slice 012h): `1.0`, `1e3` and a 19-digit
 *   integer reach the converter with their shape and digits, so its errors
 *   match STJ's. A parsed tree cannot: `JSON.parse` folds `1.0` into `1`
 *   (read as `1n`) and has already lost a 19-digit integer's digits (the
 *   beyond-safe-integer error, OQ-3 d).
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
    expect(writtenText(0n, "System.UInt64")).toBe("0");

    // A bigint has no negative zero (`-0n === 0n`), so writing `-0` into a
    // ulong slot writes `0` instead of throwing (#545).
    expect(writtenText(-0n, "System.UInt64")).toBe("0");
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
  test("readExactNumberText answers the bigint it computes for the 64-bit types", () => {
    expect(readExactNumberText("9223372036854775807", "System.Int64")).toBe(LONG_MAX);
    expect(readExactNumberText("-9223372036854775808", "System.Int64")).toBe(LONG_MIN);
    expect(readExactNumberText("18446744073709551615", "System.UInt64")).toBe(ULONG_MAX);
    expect(readExactNumberText("9007199254740993", "System.Int64")).toBe(9007199254740993n);
    expect(readExactNumberText("05", "System.UInt64")).toBe(5n);
    expect(readExactNumberText("5", "System.Int32")).toBe(5);
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

describe("int64 — dictionary values (FR-065)", () => {
  const stringKeysLongValues: TypeDescriptor = {
    kind: "map",
    key: { kind: "primitive" },
    value: { kind: "int64", numberType: "System.Int64" },
  };

  test("a 64-bit dictionary value writes an exact leaf and round-trips it (FR-057)", () => {
    const tree = JsonSerializer.serialize(new Map([["a", LONG_MAX]]), {
      descriptor: stringKeysLongValues,
    });

    expect(JSON.stringify(tree)).toBe('{"a":9223372036854775807}');
    expect(JsonSerializer.deserialize(tree, { descriptor: stringKeysLongValues })).toEqual(
      new Map([["a", LONG_MAX]]),
    );
  });

  test("a parsed 16-digit value is beyond what a number holds — the documented throw", () => {
    expect(() =>
      JsonSerializer.deserialize(JSON.parse('{"a":9223372036854775807}'), {
        descriptor: stringKeysLongValues,
      }),
    ).toThrow();
  });

  test("a 64-bit dictionary value rejects what the property path rejects", () => {
    expect(() =>
      JsonSerializer.deserialize(JSON.parse('{"a":1.5}'), {
        descriptor: stringKeysLongValues,
      }),
    ).toThrow();
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

describe("int64 — signedness on the number path (review round 1)", () => {
  test("a ulong accepts no negative number, not even -0 (measured: STJ throws)", () => {
    expectJsonException(() => readRoot(-5, "System.UInt64"), cannotConvertMessage("System.UInt64"));
    expectJsonException(() => readRoot(-1, "System.UInt64"), cannotConvertMessage("System.UInt64"));
    expectJsonException(() => readRoot(-0, "System.UInt64"), cannotConvertMessage("System.UInt64"));
  });

  test("a long reads every safe integer, negative included (measured: STJ reads them)", () => {
    expect(readRoot(9007199254740991, "System.Int64")).toBe(9007199254740991n);
    expect(readRoot(-9007199254740991, "System.Int64")).toBe(-9007199254740991n);
    expect(readRoot(-0, "System.Int64")).toBe(0n);
  });
});

describe("int64 — writes range-check (review round 1)", () => {
  test("a bigint outside the slot's range cannot be written", () => {
    expectJsonException(
      () => writtenText(2n ** 64n, "System.Int64"),
      "The value 18446744073709551616 cannot be written as System.Int64: only an exact integer "
        + "within the System.Int64 range can be written.",
    );
    expectJsonException(
      () => writtenText(-1n, "System.UInt64"),
      "The value -1 cannot be written as System.UInt64: only an exact integer within the "
        + "System.UInt64 range can be written.",
    );
  });

  test("a negative or fractional number cannot be written, a ulong's -0 included", () => {
    expectJsonException(
      () => writtenText(-5, "System.UInt64"),
      "The value -5 cannot be written as System.UInt64: only an exact integer within the "
        + "System.UInt64 range can be written.",
    );
    expectJsonException(
      () => writtenText(-0, "System.UInt64"),
      "The value 0 cannot be written as System.UInt64: only an exact integer within the "
        + "System.UInt64 range can be written.",
    );
    expectJsonException(
      () => writtenText(1.5, "System.Int64"),
      "The value 1.5 cannot be written as System.Int64: only an exact integer within the "
        + "System.Int64 range can be written.",
    );
    expect(writtenText(-5, "System.Int64")).toBe("-5");
    expect(writtenText(-0, "System.Int64")).toBe("0");
  });
});

describe("int64 — the branded factory on reads (review round 1)", () => {
  /** Stands in for a generated `[Branded]` companion's `create` (an identity at runtime). */
  const brandedInt64: TypeDescriptor = {
    kind: "int64",
    numberType: "System.Int64",
    create: (value) => ({ int64: value }),
    typeName: "MyApp.EntityId",
  };

  function readBranded(data: unknown, readOptions?: JsonReadOptions): unknown {
    return JsonSerializer.deserialize(data, { descriptor: brandedInt64, readOptions });
  }

  test("create builds the value after the exact read, on every accepted leaf", () => {
    expect(readBranded(5)).toEqual({ int64: 5n });
    expect(readBranded(9007199254740991n)).toEqual({ int64: 9007199254740991n });
    expect(readBranded(JSON.rawJSON("1152921504606846977"))).toEqual({
      int64: 1152921504606846977n,
    });
    expect(readBranded("5", READS_NUMBERS)).toEqual({ int64: 5n });
    expect(readBranded(null)).toBeNull();
  });

  test("a rejected leaf throws before the factory runs", () => {
    let builds = 0;

    const counting: TypeDescriptor = {
      kind: "int64",
      numberType: "System.Int64",
      create: (value) => {
        builds++;

        return value;
      },
    };

    expectJsonException(
      () => JsonSerializer.deserialize("x", { descriptor: counting, readOptions: READS_NUMBERS }),
      cannotConvertMessage("System.Int64"),
    );

    expect(builds).toBe(0);
  });

  test("a null element names the branded typeName, as a branded descriptor does", () => {
    const array: TypeDescriptor = { kind: "array", element: brandedInt64 };

    expectJsonException(
      () => JsonSerializer.deserialize([1, null], { descriptor: array }),
      cannotConvertMessage("MyApp.EntityId"),
    );
  });
});

describe("int64 — exact reads through deserializeText (review round 1)", () => {
  test("deserializeText reads a 64-bit root from its exact digits", () => {
    expect(JsonSerializer.deserializeText("1152921504606846977", int64Root("System.Int64"))).toBe(
      1152921504606846977n,
    );
    expect(JsonSerializer.deserializeText("-9223372036854775808", int64Root("System.Int64"))).toBe(
      LONG_MIN,
    );
    expect(
      JsonSerializer.deserializeText("18446744073709551615", int64Root("System.UInt64")),
    ).toBe(ULONG_MAX);
  });

  test("its errors are STJ's: one past the range, a fractional 19-digit leaf and strings throw", () => {
    expectJsonException(
      () => JsonSerializer.deserializeText("9223372036854775808", int64Root("System.Int64")),
      cannotConvertMessage("System.Int64"),
    );
    expectJsonException(
      () => JsonSerializer.deserializeText("1152921504606846977.0", int64Root("System.Int64")),
      cannotConvertMessage("System.Int64"),
    );
    expectJsonException(
      () => JsonSerializer.deserializeText("1152921504606846977e3", int64Root("System.Int64")),
      cannotConvertMessage("System.Int64"),
    );
    expectJsonException(
      () => JsonSerializer.deserializeText("-0", int64Root("System.UInt64")),
      cannotConvertMessage("System.UInt64"),
    );
    expectJsonException(
      () => JsonSerializer.deserializeText('"5"', int64Root("System.Int64")),
      cannotConvertMessage("System.Int64"),
    );
  });

  // Before slice 012h a text of at most 15 significant digits went through
  // `JSON.parse`, which folds `1.0` into `1` and `1e3` into `1000`, so both
  // read (divergence I5). The exact tree now keeps their shape, and the
  // integer-token rules reject them, as STJ does (measured).
  test("a short fraction or exponent throws, as STJ throws (FR-053)", () => {
    for (const text of ["1.0", "1e3", "1E0", "-0.0", "1.5", "0e0"]) {
      expectJsonException(
        () => JsonSerializer.deserializeText(text, int64Root("System.Int64")),
        cannotConvertMessage("System.Int64"),
      );
      expectJsonException(
        () => JsonSerializer.deserializeText(text, int64Root("System.UInt64")),
        cannotConvertMessage("System.UInt64"),
      );
    }
  });

  test("a short integer token still reads, -0 as 0 for a long", () => {
    expect(JsonSerializer.deserializeText("42", int64Root("System.Int64"))).toBe(42n);
    expect(JsonSerializer.deserializeText("-0", int64Root("System.Int64"))).toBe(0n);
    expect(JsonSerializer.deserializeText(" 7 ", int64Root("System.UInt64"))).toBe(7n);
  });

  test("a contract property reads its exact digits too", () => {
    class Snowflake {}

    const spec: TypeSpec = {
      type: Snowflake,
      factory: (props) => Object.assign(new Snowflake(), props),
      properties: [
        {
          ts: "id",
          json: "Id",
          type: { kind: "int64", numberType: "System.Int64" },
        } as PropertySpec,
      ],
    };

    const snowflake = JsonSerializer.deserializeText(
      '{"Id":1152921504606846977}',
      spec,
    ) as { id: bigint };

    expect(snowflake.id).toBe(1152921504606846977n);

    // The 19-digit fraction takes the exact tree, whose token rules reject
    // it naming the declaring contract.
    expectJsonException(
      () => JsonSerializer.deserializeText('{"Id":1152921504606846977.5}', spec),
      cannotConvertMessage("Snowflake"),
    );
  });
});

describe("int64 — exact reads in every position (FR-053)", () => {
  const LONG: TypeDescriptor = { kind: "int64", numberType: "System.Int64" };

  class Holder {}

  /** A contract of `properties`, every one optional, so a partial payload pins one error. */
  function holderSpec(properties: readonly Omit<PropertySpec, "optional">[]): TypeSpec {
    return {
      type: Holder,
      factory: (props) => Object.assign(new Holder(), props),
      properties: properties.map((property) => ({ ...property, optional: true })),
    };
  }

  const positions = holderSpec([
    { ts: "nullable", json: "Nullable", type: { kind: "nullable", inner: LONG } },
    { ts: "values", json: "Values", type: { kind: "array", element: LONG } },
    {
      ts: "byName",
      json: "ByName",
      type: { kind: "map", key: { kind: "primitive" }, value: LONG },
    },
    { ts: "ratio", json: "Ratio", type: { kind: "primitive", numberType: "System.Double" } },
    {
      ts: "child",
      json: "Child",
      type: { kind: "ref", spec: () => holderSpec([{ ts: "id", json: "Id", type: LONG }]) },
    },
  ]);

  test("every position reads digits beyond 2^53 from the text", () => {
    const read = JsonSerializer.deserializeText(
      '{"Nullable":9007199254740993,"Values":[-9223372036854775808,9223372036854775807],' +
        '"ByName":{"a":18014398509481985},"Child":{"Id":1152921504606846977}}',
      positions,
    ) as {
      nullable: bigint;
      values: bigint[];
      byName: Map<string, bigint>;
      child: { id: bigint };
    };

    expect(read.nullable).toBe(9007199254740993n);
    expect(read.values).toEqual([LONG_MIN, LONG_MAX]);
    expect(read.byName.get("a")).toBe(18014398509481985n);
    expect(read.child.id).toBe(1152921504606846977n);
  });

  // Each error names its position as the tree reads name it; a nullable
  // member names the nullable, where STJ names the contract (manifest I6).
  test("a short fraction or exponent throws in every position", () => {
    const rows: readonly [string, string][] = [
      ['{"Nullable":1.0}', "System.Nullable`1[System.Int64]"],
      ['{"Values":[1,2.0]}', "System.Int64"],
      ['{"ByName":{"a":1e3}}', "System.Int64"],
      ['{"Child":{"Id":5E0}}', "Holder"],
    ];

    for (const [text, typeName] of rows) {
      expectJsonException(
        () => JsonSerializer.deserializeText(text, positions),
        cannotConvertMessage(typeName),
      );
    }
  });

  test("a double member of the same contract still reads its fraction", () => {
    const read = JsonSerializer.deserializeText('{"Ratio":1.5,"Values":[2]}', positions) as {
      ratio: number;
      values: bigint[];
    };

    expect(read.ratio).toBe(1.5);
    expect(read.values).toEqual([2n]);
  });

  test("a nullable root reads null and rejects a fraction naming the nullable", () => {
    const nullableRoot: ValueSpec = {
      descriptor: { kind: "nullable", inner: LONG },
      typeName: "System.Nullable`1[System.Int64]",
    };

    expect(JsonSerializer.deserializeText("null", nullableRoot)).toBeNull();
    expect(JsonSerializer.deserializeText("9007199254740993", nullableRoot)).toBe(
      9007199254740993n,
    );

    expectJsonException(
      () => JsonSerializer.deserializeText("1.0", nullableRoot),
      cannotConvertMessage("System.Nullable`1[System.Int64]"),
    );
  });

  test("a branded root applies its factory to the exact read and rejects a fraction", () => {
    const branded: ValueSpec = {
      descriptor: {
        kind: "int64",
        numberType: "System.Int64",
        create: (value) => ({ int64: value }),
        typeName: "MyApp.EntityId",
      },
    };

    expect(JsonSerializer.deserializeText("1152921504606846977", branded)).toEqual({
      int64: 1152921504606846977n,
    });

    expect(() => JsonSerializer.deserializeText("1.0", branded)).toThrow(JsonException);
  });

  // A custom converter gets the whole subtree with doubles, as for decimals:
  // the exact leaves are an internal detail of the built-in readers.
  test("a custom converter of a sibling kind sees doubles, not number texts", () => {
    const doubling: JsonConverter = {
      kind: "array",
      serialize: (value) => value,
      deserialize: (value) => (value as number[]).map((item) => item * 2),
    };

    const spec = holderSpec([
      { ts: "id", json: "Id", type: LONG },
      { ts: "list", json: "List", type: { kind: "array", element: { kind: "primitive" } } },
    ]);

    const read = JsonSerializer.deserializeText('{"Id":3,"List":[1.5,2e0]}', spec, [doubling]) as {
      id: bigint;
      list: number[];
    };

    expect(read.id).toBe(3n);
    expect(read.list).toEqual([3, 4]);
  });
});

describe("int64 — the exact tree of a 64-bit closure (FR-053)", () => {
  const LONG: TypeDescriptor = { kind: "int64", numberType: "System.Int64" };

  class Shape {}

  class Circle {}

  const circle: TypeSpec = {
    type: Circle,
    factory: (props) => Object.assign(new Circle(), props),
    properties: [{ ts: "id", json: "Id", type: LONG } as PropertySpec],
  };

  const shape: TypeSpec = {
    type: Shape,
    factory: (props) => Object.assign(new Shape(), props),
    properties: [],
    polymorphism: { discriminator: "$type", derived: [{ tag: 1, spec: () => circle }] },
  };

  test("a 64-bit integer anywhere in the closure asks for non-integer leaves too", () => {
    const decimalOnly: ValueSpec = { descriptor: { kind: "decimal" } };

    const mixed: ValueSpec = {
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: { kind: "array", element: { kind: "nullable", inner: LONG } },
      },
    };

    expect(specNumberTextLeaves(decimalOnly)).toBe("inexact");
    expect(specNumberTextLeaves(mixed)).toBe("inexactOrNonInteger");
    expect(specNumberTextLeaves(shape)).toBe("inexactOrNonInteger");
  });

  // STJ rejects an int tag written `1.0` or `1e0` (measured, .NET 10); a
  // parsed tree cannot tell it from `1`, so `deserialize` still accepts it.
  test("an int tag with a fraction or an exponent throws through the exact tree", () => {
    for (const text of ['{"$type":1.0,"Id":5}', '{"$type":1e0,"Id":5}']) {
      expectJsonException(
        () => JsonSerializer.deserializeText(text, shape),
        cannotConvertMessage("Shape"),
      );
    }

    const read = JsonSerializer.deserializeText('{"$type":1,"Id":5}', shape) as { id: bigint };

    expect(read).toBeInstanceOf(Circle);
    expect(read.id).toBe(5n);
  });
});
