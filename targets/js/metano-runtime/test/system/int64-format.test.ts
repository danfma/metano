import { describe, expect, test } from "bun:test";
import { FormatException, NotSupportedException } from "#/system/exceptions/index.ts";
import { INT64_MIN, UINT64_MAX } from "#/system/int64.ts";
import { formatInt64, formatInteger } from "#/system/int64-format.ts";

/** Every expected text below was printed by `ToString(format)` on .NET 10.0.12 (invariant culture). */
describe("formatInt64", () => {
  const rows: [bigint, string | null, string][] = [
    [-1234567n, null, "-1234567"],
    [-1234567n, "", "-1234567"],
    [INT64_MIN, "G", "-9223372036854775808"],
    [-1234567n, "G5", "-1.2346E+06"],
    [1250000n, "G2", "1.3E+06"],
    [999999n, "G2", "1E+06"],
    [1250000n, "g5", "1.25e+06"],
    [INT64_MIN, "G7", "-9.223372E+18"],
    [UINT64_MAX, "G19", "1.844674407370955162E+19"],
    [UINT64_MAX, "G20", "18446744073709551615"],
    [-1234567n, "R", "-1234567"],
    [-1234567n, "D8", "-01234567"],
    [255n, "d3", "255"],
    [-1234567n, "N", "-1,234,567.00"],
    [INT64_MIN, "N0", "-9,223,372,036,854,775,808"],
    [255n, "n3", "255.000"],
    [0n, "N0", "0"],
    [-1234567n, "F", "-1234567.00"],
    [255n, "F0", "255"],
    [-1234567n, "X", "FFFFFFFFFFED2979"],
    [INT64_MIN, "X", "8000000000000000"],
    [255n, "x4", "00ff"],
    [255n, "X20", "000000000000000000FF"],
    [UINT64_MAX, "X", "FFFFFFFFFFFFFFFF"],
    [0n, "X0", "0"],
    [25n, "E0", "3E+001"],
    [-3n, "E", "-3.000000E+000"],
    [12n, "e2", "1.20e+001"],
    [5n, "P0", "500 %"],
    [-3n, "P", "-300.00 %"],
    [12n, "C", "\u00a412.00"],
    [-1n, "C", "(\u00a41.00)"],
  ];

  test.each(rows)("%p with %p is %p", (value, format, expected) => {
    expect(formatInt64(value, format)).toBe(expected);
  });

  test("an alignment pads with spaces, on the left when positive", () => {
    expect(formatInt64(5n, null, 8)).toBe("       5");
    expect(formatInt64(5n, null, -8)).toBe("5       ");
    expect(formatInt64(-5n, "D3", 8)).toBe("    -005");
    expect(formatInt64(255n, "X", 4)).toBe("  FF");
    expect(formatInt64(5n, null, 0)).toBe("5");
  });

  test("a null value formats as the empty string, then pads", () => {
    expect(formatInt64(null, "D2")).toBe("");
    expect(formatInt64(null, "D2", 3)).toBe("   ");
  });

  test("valid .NET formats outside the implemented set throw NotSupportedException", () => {
    for (const format of ["B", "b8", "#,##0", "0", " ", "X-1"]) {
      expect(() => formatInt64(1n, format)).toThrow(NotSupportedException);
    }
  });

  test("invalid formats throw FormatException", () => {
    expect(() => formatInt64(1n, "Q")).toThrow(new FormatException("Format specifier was invalid."));
    expect(() => formatInt64(1n, "D1000000000")).toThrow(FormatException);
  });
});

/** Every expected text below was printed by `ToString(format)` on .NET 10.0.12 (invariant culture). */
describe("formatInteger", () => {
  const rows: [number, 8 | 16 | 32, string | null, string][] = [
    [-3, 32, null, "-3"],
    [3, 32, "D2", "03"],
    [-5, 32, "D3", "-005"],
    [-3, 32, "X", "FFFFFFFD"],
    [-3, 32, "x8", "fffffffd"],
    [-2147483648, 32, "X", "80000000"],
    [4294967295, 32, "X", "FFFFFFFF"],
    [-2, 16, "X", "FFFE"],
    [65535, 16, "X", "FFFF"],
    [-1, 8, "X", "FF"],
    [7, 8, "X2", "07"],
    [-128, 8, "D5", "-00128"],
    [-2147483648, 32, "N", "-2,147,483,648.00"],
    [12, 32, "n1", "12.0"],
    [12, 32, "f1", "12.0"],
    [2147483647, 32, "E2", "2.15E+009"],
    [25, 32, "E0", "3E+001"],
    [-25, 32, "E0", "-3E+001"],
    [0, 32, "E", "0.000000E+000"],
    [25, 32, "G1", "3E+01"],
    [125, 32, "G2", "1.3E+02"],
    [255, 32, "G2", "2.6E+02"],
    [12, 32, "p", "1,200.00 %"],
    [5, 32, "P0", "500 %"],
    [-1, 32, "C", "(\u00a41.00)"],
    [12, 32, "C0", "\u00a412"],
    [2147483647, 32, "R5", "2.1475E+09"],
    [-1234567, 32, "R5", "-1.2346E+06"],
  ];

  test.each(rows)("%p on %p bits with %p is %p", (value, bits, format, expected) => {
    expect(formatInteger(value, bits, format)).toBe(expected);
  });

  test("an alignment pads the formatted text, on the right when negative", () => {
    expect(formatInteger(3, 32, null, 8)).toBe("       3");
    expect(formatInteger(3, 32, "D2", -8)).toBe("03      ");
  });

  test("a null value formats as the empty string, then pads", () => {
    expect(formatInteger(null, 32, "D2")).toBe("");
    expect(formatInteger(null, 32, null, 4)).toBe("    ");
  });

  test("valid .NET formats outside the implemented set throw NotSupportedException", () => {
    for (const format of ["B", "#,##0", "0.00", "F2 "]) {
      expect(() => formatInteger(1, 32, format)).toThrow(NotSupportedException);
    }
  });

  test("invalid formats throw FormatException", () => {
    expect(() => formatInteger(3, 32, "Z")).toThrow(new FormatException("Format specifier was invalid."));
    expect(() => formatInteger(3, 32, "D1000000000")).toThrow(FormatException);
  });
});
