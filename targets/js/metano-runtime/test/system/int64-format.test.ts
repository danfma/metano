import { describe, expect, test } from "bun:test";
import { FormatException, NotSupportedException } from "#/system/exceptions/index.ts";
import { INT64_MIN, UINT64_MAX } from "#/system/int64.ts";
import { formatInt64 } from "#/system/int64-format.ts";

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

  test("valid .NET formats outside the implemented set throw NotSupportedException", () => {
    for (const format of ["C", "P", "E", "e3", "B", "#,##0", "0", " ", "X-1"]) {
      expect(() => formatInt64(1n, format)).toThrow(NotSupportedException);
    }
  });

  test("invalid formats throw FormatException", () => {
    expect(() => formatInt64(1n, "Q")).toThrow(new FormatException("Format specifier was invalid."));
    expect(() => formatInt64(1n, "D1000000000")).toThrow(FormatException);
  });
});
