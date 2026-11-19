import { describe, expect, test } from "bun:test";
import { FormatException, NotSupportedException } from "#/system/exceptions/index.ts";
import { formatInteger, type IntegerBits } from "#/system/integer-format.ts";

/**
 * Every expected text below was printed by `ToString(format)` on .NET 10.0.12 (invariant culture).
 */
describe("formatInteger", () => {
  const rows: [number, IntegerBits, string | null, string][] = [
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

  test("a null format at run time is G, as ToString((string?)null) is on .NET", () => {
    expect(formatInteger(-1234567, 32, null)).toBe("-1234567");
  });

  test("an unimplemented format names itself in the NotSupportedException", () => {
    expect(() => formatInteger(5, 32, "B")).toThrow(
      new NotSupportedException("The format 'B' is not supported for integers in TypeScript."),
    );

    expect(() => formatInteger(5, 32, "0.00")).toThrow(
      new NotSupportedException("The format '0.00' is not supported for integers in TypeScript."),
    );
  });

  test("valid .NET formats outside the implemented set throw NotSupportedException", () => {
    for (const format of ["#,##0", "F2 "]) {
      expect(() => formatInteger(1, 32, format)).toThrow(NotSupportedException);
    }
  });

  test("invalid formats throw FormatException", () => {
    expect(() => formatInteger(3, 32, "Z")).toThrow(
      new FormatException("Format specifier was invalid."),
    );

    expect(() => formatInteger(3, 32, "D1000000000")).toThrow(FormatException);
  });
});
