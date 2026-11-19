import { describe, expect, test } from "bun:test";
import { Decimal } from "#/system/decimal";
import { OverflowException } from "#/system/exceptions";
import { narrowFromDecimal } from "#/system/int64";

describe("decimal to narrow integer", () => {
  test.each([NaN, Infinity, -Infinity])("rejects non-finite decimal %s with the target message", (value) => {
    expect(() => narrowFromDecimal(new Decimal(value), 8, false)).toThrow(
      new OverflowException("Value was either too large or too small for an unsigned byte."),
    );
  });

  test.each([
    [8, false, "255.9", 255],
    [8, true, "-128.9", -128],
    [16, false, "65535.9", 65535],
    [16, true, "-32768.9", -32768],
    [32, false, "4294967295.9", 4294967295],
    [32, true, "2147483647.9", 2147483647],
    [8, false, "-0.9", 0],
  ] as const)("truncates %s-bit signed=%s %s", (bits, signed, text, expected) => {
    expect(narrowFromDecimal(new Decimal(text), bits, signed)).toBe(expected);
  });

  test.each([
    [8, false, "256", "an unsigned byte"],
    [8, true, "-129", "a signed byte"],
    [16, false, "65536", "a UInt16"],
    [16, true, "-32769", "an Int16"],
    [32, false, "4294967296", "a UInt32"],
    [32, true, "2147483648", "an Int32"],
    [32, true, "79228162514264337593543950335", "an Int32"],
  ] as const)("checks %s-bit signed=%s %s", (bits, signed, text, type) => {
    expect(() => narrowFromDecimal(new Decimal(text), bits, signed)).toThrow(
      new OverflowException(`Value was either too large or too small for ${type}.`),
    );
  });
});
