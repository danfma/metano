import { describe, expect, test } from "bun:test";
import { formatDouble } from "#/system/double-format.ts";
import { FormatException, NotSupportedException } from "#/system/exceptions/index.ts";

/**
 * Every expected text below was printed by `ToString(format, InvariantCulture)`
 * on .NET 10 (the reference fixture `number-formatting.json` covers the full
 * matrix under bun and Node).
 */
describe("formatDouble", () => {
  const rows: [number, string | null, string][] = [
    [2.675, "F2", "2.67"],
    [1.005, "F2", "1.00"],
    [0.125, "F2", "0.12"],
    [0.375, "F2", "0.38"],
    [0.5, "F0", "0"],
    [1.5, "F0", "2"],
    [2.5, "F0", "2"],
    [-0.5, "F0", "-0"],
    [-3.5, "F0", "-4"],
    [-0.001, "F2", "-0.00"],
    [-0, "F2", "-0.00"],
    [1e23, "F0", "99999999999999991611392"],
    [2.675, "F20", "2.67499999999999982236"],
    [12345.6789, "N2", "12,345.68"],
    [-0.001, "N2", "-0.00"],
    [2.675, "E", "2.675000E+000"],
    [2.5, "E0", "2E+000"],
    [5e-324, "E20", "4.94065645841246544177E-324"],
    [-0.001, "E2", "-1.00E-003"],
    [12345.6789, "e3", "1.235e+004"],
    [0.045, "P0", "4 %"],
    [0.025, "P0", "3 %"],
    [-0.5, "P", "-50.00 %"],
    [-0.001, "P0", "-0 %"],
    [12345.6789, "C", "¤12,345.68"],
    [-0.5, "C", "(¤0.50)"],
    [-0.001, "C", "(¤0.00)"],
    [0.30000000000000004, null, "0.30000000000000004"],
    [0.30000000000000004, "G", "0.30000000000000004"],
    [1e16, "G", "10000000000000000"],
    [123456789012345678, "G", "1.2345678901234568E+17"],
    [1e21, "R", "1E+21"],
    [1e-5, "G", "1E-05"],
    [0.0001, "G", "0.0001"],
    [-0, "G", "-0"],
    [0.1, "G0", "0.1"],
    [0.1, "R5", "0.1"],
    [12345.6789, "G3", "1.23E+04"],
    [2.5, "G1", "2"],
    [0.0005, "G3", "0.0005"],
    [1e-5, "G3", "1E-05"],
    [100, "G2", "1E+02"],
    [0.1, "G20", "0.10000000000000000555"],
    [Number.NaN, "F2", "NaN"],
    [Number.POSITIVE_INFINITY, "N", "Infinity"],
    [Number.NEGATIVE_INFINITY, "E2", "-Infinity"],
    [Number.NaN, "D", "NaN"],
    [Number.POSITIVE_INFINITY, "0.00", "Infinity"],
  ];

  test.each(rows)("%p with %p is %p", (value, format, expected) => {
    expect(formatDouble(value, format)).toBe(expected);
  });

  test("an alignment pads the formatted text, on the right when negative", () => {
    expect(formatDouble(2.5, "F2", 8)).toBe("    2.50");
    expect(formatDouble(2.5, "F2", -8)).toBe("2.50    ");
  });

  test("a null value formats as the empty string, then pads", () => {
    expect(formatDouble(null, "F2")).toBe("");
    expect(formatDouble(null, "F2", 3)).toBe("   ");
  });

  test("a null format at run time is G, as ToString((string?)null) is on .NET", () => {
    expect(formatDouble(1e-7, null)).toBe("1E-07");
    expect(formatDouble(0.1 + 0.2, undefined)).toBe("0.30000000000000004");
  });

  test("a custom format names itself in the NotSupportedException", () => {
    expect(() => formatDouble(1.5, "0.00")).toThrow(
      new NotSupportedException("The format '0.00' is not supported for double in TypeScript."),
    );
  });

  test("custom formats throw NotSupportedException", () => {
    for (const format of ["0.00", "#,##0", "F2,5", " F2"]) {
      expect(() => formatDouble(1.5, format)).toThrow(NotSupportedException);
    }
  });

  test("formats .NET rejects for double throw FormatException", () => {
    expect(() => formatDouble(1.5, "D")).toThrow(new FormatException("Format specifier was invalid."));
    expect(() => formatDouble(1.5, "X")).toThrow(FormatException);
    expect(() => formatDouble(1.5, "F1000000000")).toThrow(FormatException);
  });
});
