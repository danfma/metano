import { describe, expect, test } from "bun:test";
import {
  type JsonReadOptions,
  JsonSerializer,
  type NumberTypeName,
  type TypeDescriptor,
  type ValueSpec,
} from "#/system/json/index.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";

/**
 * Numbers read from JSON strings (spec 009 FR-048 item 4,
 * `JsonNumberHandling.AllowReadingFromString`). Every row below was measured
 * with a source-generated `JsonSerializerDefaults.Web` context (.NET SDK
 * 10.0.401, runtime 10.0.12); messages without the `Path` suffix:
 *
 * - Int32: `"5"`, `"+5"`, `"-5"`, `"007"`, `"-007"`, `"+007"`, `"0005"`, `"00"`
 *   read; `"-0"` and `"+0"` read `0`; `"5.0"`, `" 5"`, `"5 "`, `"\t5"`,
 *   `"5\n"`, `""`, `"1e2"`, `"05e0"`, `"0x10"`, `"1_0"`, `"1,000"`, `"١"`,
 *   `"--5"`, `"+-5"`, `"-"`, `"+"` throw "The JSON value could not be
 *   converted to System.Int32.".
 * - Signed ranges are exact (`SByte` 127 / -128, `Int16`, `Int32`, `Int64`
 *   9223372036854775807 / -9223372036854775808; one past either end throws),
 *   and leading zeros never count (`"000…0002147483647"` reads).
 * - Unsigned types (`Byte`, `UInt16`, `UInt32`, `UInt64`) take no sign at all:
 *   `"+5"`, `"+0"`, `"-0"` and `"-1"` throw; `"0255"` reads `255` as a Byte.
 * - Double and Single: `"2.5"`, `"1e2"`, `"1E2"`, `"1E+2"`, `"1e-2"`,
 *   `"1e+05"`, `"+1.5"`, `"-0"` (reads `-0`), `"1."`, `"0."`, `".5"`, `"+.5"`,
 *   `"-.5"`, `"1.e5"`, `".5e1"`, `"01.5"`, `"0000.5"`, `"NaN"`, `"Infinity"`,
 *   `"-Infinity"` read; `"1e-400"` reads `0`; `"+Infinity"`, `"-NaN"`,
 *   `"nan"`, `"NAN"`, `"infinity"`, `" 1.5"`, `"1.5 "`, `"Infinity "`,
 *   `"0x1"`, `"."`, `"-."`, `".e1"`, `"1e"`, `"1e+"`, `"e5"`, `"1e2.5"`,
 *   `""`, `"1,5"`, `"1d"`, `"1f"`, `"1_0"`, `"∞"`, `"١.٥"` throw; a value
 *   that overflows the type throws (`"1e400"`; `"1.7976931348623159e308"`
 *   for Double; `"3.4028236e38"` and `"1e39"` for Single, whose
 *   `"3.4028235677973366e38"` and `"340282356779733661637539395458142568447"`
 *   read while the exact midpoint `"340282356779733661637539395458142568448"`
 *   (2^128 − 2^103), one past it and `"3.4028235677973367e38"` throw; and
 *   `"3.4028235e38"` reads).
 * - Decimal: the floating-point text without `NaN` / `Infinity`
 *   (`"1.10"`, `"1e2"`, `".5"`, `"1."`, `"+.5"`, `"1.e2"`, `"01.5"` read);
 *   `±79228162514264337593543950335` and `"79228162514264337593543950335.4"`
 *   read, `"…336"`, `"…335.5"` and `"1e29"` throw; `"1e-30"` reads 0.
 * - Strict contexts (`JsonSerializerDefaults.General`, or `Web` with
 *   `NumberHandling = Strict`) throw for `"5"`.
 */

const READS_NUMBERS: JsonReadOptions = { numberHandling: "allowReadingFromString" };

function numberRoot(numberType: NumberTypeName, readOptions = READS_NUMBERS): ValueSpec<unknown> {
  return { descriptor: { kind: "primitive", numberType }, readOptions };
}

function readRoot(data: unknown, descriptor: TypeDescriptor): unknown {
  return JsonSerializer.deserialize(data, { descriptor, readOptions: READS_NUMBERS });
}

/** Asserts every text reads as the paired number (`Object.is`, so `-0` counts). */
function expectReads(numberType: NumberTypeName, rows: readonly [string, number][]): void {
  for (const [text, expected] of rows) {
    expect([text, JsonSerializer.deserialize(text, numberRoot(numberType))]).toEqual([
      text,
      expected,
    ]);
  }
}

function expectRejects(numberType: NumberTypeName, texts: readonly string[]): void {
  for (const text of texts) {
    expectJsonException(
      () => JsonSerializer.deserialize(text, numberRoot(numberType)),
      cannotConvertMessage(numberType),
    );
  }
}

const INTEGER_REJECTED = [
  "5.0",
  " 5",
  "5 ",
  "\t5",
  "5\n",
  "",
  "1e2",
  "05e0",
  "0x10",
  "1_0",
  "1,000",
  "١",
  "--5",
  "+-5",
  "-",
  "+",
];

describe("numbers from strings — signed integers", () => {
  test("an optional sign and leading zeros are accepted", () => {
    expectReads("System.Int32", [
      ["5", 5],
      ["+5", 5],
      ["-5", -5],
      ["007", 7],
      ["-007", -7],
      ["+007", 7],
      ["0005", 5],
      ["00", 0],
      ["00000000000000000000000000000000000002147483647", 2_147_483_647],
    ]);
  });

  test("-0 and +0 read as 0, never as -0", () => {
    expectReads("System.Int32", [
      ["-0", 0],
      ["+0", 0],
    ]);
    expectReads("System.SByte", [["-0", 0]]);
    expectReads("System.Int64", [["-0", 0]]);
  });

  test("any other spelling is rejected", () => {
    expectRejects("System.Int32", INTEGER_REJECTED);
  });

  test("the range of each type is exact", () => {
    expectReads("System.SByte", [
      ["127", 127],
      ["-128", -128],
      ["+5", 5],
    ]);
    expectRejects("System.SByte", ["128", "-129"]);
    expectReads("System.Int16", [
      ["32767", 32_767],
      ["-32768", -32_768],
      ["+5", 5],
    ]);
    expectRejects("System.Int16", ["32768", "-32769"]);
    expectReads("System.Int32", [
      ["2147483647", 2_147_483_647],
      ["-2147483648", -2_147_483_648],
    ]);
    expectRejects("System.Int32", ["2147483648", "-2147483649"]);
    expectReads("System.Int64", [
      ["9223372036854775807", 9_223_372_036_854_775_807],
      ["-9223372036854775808", -9_223_372_036_854_775_808],
      ["+5", 5],
    ]);
    expectRejects("System.Int64", ["9223372036854775808", "-9223372036854775809"]);
  });

  test("an Int64 beyond 2^53 reads as JSON.parse reads the same number", () => {
    expectReads("System.Int64", [["9007199254740993", JSON.parse("9007199254740993")]]);
  });
});

describe("numbers from strings — unsigned integers", () => {
  test("digits only: no sign, not even +0 or -0", () => {
    expectReads("System.Byte", [
      ["5", 5],
      ["0255", 255],
      ["255", 255],
    ]);
    expectRejects("System.Byte", ["+5", "+255", "-0", "-1", "256", ...INTEGER_REJECTED]);
    expectReads("System.UInt16", [["65535", 65_535]]);
    expectRejects("System.UInt16", ["+5", "-0", "-1", "65536"]);
    expectReads("System.UInt32", [["4294967295", 4_294_967_295]]);
    expectRejects("System.UInt32", ["+5", "-0", "-1", "4294967296"]);
    expectReads("System.UInt64", [["18446744073709551615", 18_446_744_073_709_551_615]]);
    expectRejects("System.UInt64", ["+5", "+0", "-0", "-1", "18446744073709551616"]);
  });
});

const FLOATING_POINT_READ: [string, number][] = [
  ["2.5", 2.5],
  ["1e2", 100],
  ["1E2", 100],
  ["1E+2", 100],
  ["1e-2", 0.01],
  ["1e+05", 100_000],
  ["1E-0", 1],
  ["+1.5", 1.5],
  ["-1.5", -1.5],
  ["-0", -0],
  ["-0.0", -0],
  ["+0", 0],
  ["00", 0],
  ["5", 5],
  ["1.", 1],
  ["0.", 0],
  [".5", 0.5],
  ["+.5", 0.5],
  ["-.5", -0.5],
  ["1.e5", 100_000],
  [".5e1", 5],
  ["01.5", 1.5],
  ["0000.5", 0.5],
  ["1e00000000000000000002", 100],
  ["1e-400", 0],
  ["NaN", Number.NaN],
  ["Infinity", Number.POSITIVE_INFINITY],
  ["-Infinity", Number.NEGATIVE_INFINITY],
];

const FLOATING_POINT_REJECTED = [
  "+Infinity",
  "-NaN",
  "+NaN",
  "nan",
  "NAN",
  "infinity",
  " 1.5",
  "1.5 ",
  "Infinity ",
  "0x1",
  ".",
  "-.",
  ".e1",
  "+",
  "-",
  "+-1",
  "1e",
  "1e+",
  "1.5e+",
  "e5",
  "1e2.5",
  "",
  "1,5",
  "1d",
  "1f",
  "1_0",
  "∞",
  "١.٥",
  "1e400",
  "-1e400",
  "1e0400",
];

describe("numbers from strings — floating point", () => {
  test("Double reads the floating-point grammar and the exact named literals", () => {
    expectReads("System.Double", [
      ...FLOATING_POINT_READ,
      ["1.7976931348623157e308", 1.7976931348623157e308],
    ]);
  });

  test("Double rejects other spellings and overflow", () => {
    expectRejects("System.Double", [...FLOATING_POINT_REJECTED, "1.7976931348623159e308"]);
  });

  test("Single reads the same grammar and keeps the double value", () => {
    expectReads("System.Single", [
      ...FLOATING_POINT_READ,
      ["3.4028235e38", 3.4028235e38],
      ["0.1", 0.1],
    ]);
  });

  test("Single rejects a value beyond its own range", () => {
    expectRejects("System.Single", [
      ...FLOATING_POINT_REJECTED,
      "3.4028236e38",
      "3.5e38",
      "1e39",
      "1.7976931348623157e308",
    ]);
  });

  // The overflow threshold 2^128 − 2^103 is itself a double, so these texts
  // all parse to it; only the exact text tells them apart (measured).
  test("Single decides the overflow threshold by the exact text", () => {
    expectReads("System.Single", [
      ["3.4028235677973366e38", 3.4028235677973366e38],
      ["-3.4028235677973366e38", -3.4028235677973366e38],
      ["3.40282356779733661e38", 3.40282356779733661e38],
      ["340282356779733661637539395458142568447", 3.4028235677973366e38],
    ]);
    expectRejects("System.Single", [
      "340282356779733661637539395458142568448",
      "-340282356779733661637539395458142568448",
      "3.40282356779733661637539395458142568448e38",
      "340282356779733661637539395458142568449",
      "3.4028235677973367e38",
    ]);
  });
});

describe("numbers from strings — decimal", () => {
  const decimalRoot: TypeDescriptor = { kind: "decimal" };

  test("accepted text is returned unchanged, keeping precision and scale", () => {
    const accepted = [
      "1.10",
      "1e2",
      "1E2",
      "1e-2",
      "+1.5",
      "-0",
      "-0.00",
      ".5",
      "+.5",
      "1.",
      "1.e2",
      "01.5",
      "5",
      "79228162514264337593543950335",
      "-79228162514264337593543950335",
      "79228162514264337593543950335.4",
      "7.9228162514264337593543950335e28",
      "1e28",
      "1e-28",
      "1e-30",
      "0.00000000000000000000000000001",
    ];

    for (const text of accepted) {
      expect(readRoot(text, decimalRoot)).toBe(text);
    }
  });

  test("named literals, other spellings and overflow are rejected", () => {
    const rejected = [
      "NaN",
      "Infinity",
      " 1",
      "1 ",
      "",
      "0x1",
      ".",
      "1e",
      "-",
      "79228162514264337593543950336",
      "-79228162514264337593543950336",
      "79228162514264337593543950335.5",
      "1e29",
    ];

    for (const text of rejected) {
      expectJsonException(() => readRoot(text, decimalRoot), cannotConvertMessage("System.Decimal"));
    }
  });

  test("a JSON number still reads as before", () => {
    expect(readRoot(1.5, decimalRoot)).toBe(1.5);
  });

  test("a JSON number outside the decimal range, or any other JSON value, is rejected", () => {
    for (const value of [1e29, -1e29, true, {}]) {
      expectJsonException(() => readRoot(value, decimalRoot), cannotConvertMessage("System.Decimal"));
    }
  });
});

describe("decimal — values built by the descriptor's type", () => {
  /** Stands in for decimal.js `Decimal`, which the runtime does not depend on. */
  class ExactDecimal {
    constructor(readonly source: string | number) {}

    toNumber(): number {
      return Number(this.source);
    }
  }

  const descriptor: TypeDescriptor = { kind: "decimal", type: ExactDecimal };

  function readDecimal(data: unknown, readOptions?: JsonReadOptions): unknown {
    return JsonSerializer.deserialize(data, { descriptor, readOptions });
  }

  test("a JSON number builds a value from the number", () => {
    expect(readDecimal(1.25)).toEqual(new ExactDecimal(1.25));
  });

  test("a JSON string builds a value from the exact text when the context reads strings", () => {
    expect(readDecimal("1.10", READS_NUMBERS)).toEqual(new ExactDecimal("1.10"));
    expect(readDecimal("79228162514264337593543950335", READS_NUMBERS)).toEqual(
      new ExactDecimal("79228162514264337593543950335"),
    );
  });

  test("a string is rejected without the option, and null stays null", () => {
    expectJsonException(() => readDecimal("1.10"), cannotConvertMessage("System.Decimal"));
    expect(readDecimal(null)).toBeNull();
  });

  test("the value round-trips through toNumber()", () => {
    const value = readDecimal("2.5", READS_NUMBERS);

    expect(JsonSerializer.serialize(value, { descriptor })).toBe(2.5);
  });
});

describe("numbers from strings — where the option applies", () => {
  test("collection elements and dictionary values read strings", () => {
    const int32: TypeDescriptor = { kind: "primitive", numberType: "System.Int32" };

    expect(readRoot(["1", 2], { kind: "array", element: int32 })).toEqual([1, 2]);

    expect(
      readRoot({ k: "3" }, { kind: "map", key: { kind: "primitive" }, value: int32 }),
    ).toEqual(new Map([["k", 3]]));

    expectJsonException(
      () => readRoot({ k: "5.0" }, { kind: "map", key: { kind: "primitive" }, value: int32 }),
      cannotConvertMessage("System.Int32"),
    );

    expectJsonException(
      () => readRoot(["x"], { kind: "array", element: int32 }),
      cannotConvertMessage("System.Int32"),
    );
  });

  test("dictionary keys are left as they are", () => {
    const map: TypeDescriptor = {
      kind: "map",
      key: { kind: "primitive", numberType: "System.Int32" },
      value: { kind: "primitive", numberType: "System.Int32" },
    };

    expect(readRoot({ "007": "1" }, map)).toEqual(new Map([["007", 1]]));
  });

  test("a nullable number reads strings, but not the text null", () => {
    const nullableInt32: TypeDescriptor = {
      kind: "nullable",
      inner: { kind: "primitive", numberType: "System.Int32" },
    };

    const nullableName = "System.Nullable`1[System.Int32]";

    expect(readRoot("5", nullableInt32)).toBe(5);
    expect(readRoot(null, nullableInt32)).toBeNull();
    expectJsonException(() => readRoot("null", nullableInt32), cannotConvertMessage(nullableName));
    expectJsonException(() => readRoot("x", nullableInt32), cannotConvertMessage(nullableName));
    expectJsonException(() => readRoot("5.5", nullableInt32), cannotConvertMessage(nullableName));

    expect(readRoot(["5", null], { kind: "array", element: nullableInt32 })).toEqual([5, null]);
    expectJsonException(
      () => readRoot(["null"], { kind: "array", element: nullableInt32 }),
      cannotConvertMessage(nullableName),
    );
  });

  test("a nullable decimal names System.Nullable`1[System.Decimal]", () => {
    expectJsonException(
      () => readRoot("x", { kind: "nullable", inner: { kind: "decimal" } }),
      cannotConvertMessage("System.Nullable`1[System.Decimal]"),
    );
  });

  test("enums never read strings", () => {
    const levels = { Low: 0, High: 1, 0: "Low", 1: "High" };

    expect(() => readRoot("1", { kind: "numericEnum", values: levels })).toThrow();
  });

  test("a primitive without numberType (a string) is unaffected", () => {
    expect(readRoot("007", { kind: "primitive" })).toBe("007");
  });

  test("without the option, strings pass through as before", () => {
    const strict = numberRoot("System.Int32", {});

    expect(JsonSerializer.deserialize("5.5", strict)).toBe("5.5");
  });

  test("without the option, a decimal rejects every string (measured: General, \"1\")", () => {
    expectJsonException(
      () => JsonSerializer.deserialize("1", { descriptor: { kind: "decimal" } }),
      cannotConvertMessage("System.Decimal"),
    );
  });

  test("writing is unaffected (FR-048 item 5)", () => {
    expect(JsonSerializer.serialize(5, numberRoot("System.Int32"))).toBe(5);
  });

  test("a custom converter for the kind still replaces the built-in one", () => {
    const value = JsonSerializer.deserialize("x", numberRoot("System.Int32"), [
      { kind: "primitive", serialize: (v) => v, deserialize: (v) => `custom:${String(v)}` },
    ]);

    expect(value).toBe("custom:x");
  });
});
