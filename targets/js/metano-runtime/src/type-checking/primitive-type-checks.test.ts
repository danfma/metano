import { describe, expect, test } from "bun:test";
import {
  isBigInt,
  isBool,
  isByte,
  isChar,
  isFloat32,
  isFloat64,
  isInt16,
  isInt32,
  isInt64,
  isSByte,
  isString,
  isUInt16,
  isUInt32,
  isUInt64,
} from "./primitive-type-checks";

describe("String type checks", () => {
  test("isChar accepts single character", () => {
    expect(isChar("a")).toBe(true);
    expect(isChar("Z")).toBe(true);
  });

  test("isChar rejects multi-char and empty strings", () => {
    expect(isChar("")).toBe(false);
    expect(isChar("ab")).toBe(false);
    expect(isChar("hello")).toBe(false);
  });

  test("isChar rejects non-strings", () => {
    expect(isChar(42)).toBe(false);
    expect(isChar(null)).toBe(false);
    expect(isChar(undefined)).toBe(false);
  });

  test("isString accepts any string", () => {
    expect(isString("")).toBe(true);
    expect(isString("hello")).toBe(true);
    expect(isString("a")).toBe(true);
  });

  test("isString rejects non-strings", () => {
    expect(isString(42)).toBe(false);
    expect(isString(true)).toBe(false);
    expect(isString(null)).toBe(false);
  });
});

describe("Integer type checks", () => {
  test("isByte accepts 0..255", () => {
    expect(isByte(0)).toBe(true);
    expect(isByte(128)).toBe(true);
    expect(isByte(255)).toBe(true);
  });

  test("isByte rejects out of range", () => {
    expect(isByte(-1)).toBe(false);
    expect(isByte(256)).toBe(false);
    expect(isByte(1.5)).toBe(false);
  });

  test("isSByte accepts -128..127", () => {
    expect(isSByte(-128)).toBe(true);
    expect(isSByte(0)).toBe(true);
    expect(isSByte(127)).toBe(true);
    expect(isSByte(-129)).toBe(false);
    expect(isSByte(128)).toBe(false);
  });

  test("isInt16 accepts -32768..32767", () => {
    expect(isInt16(-32768)).toBe(true);
    expect(isInt16(32767)).toBe(true);
    expect(isInt16(-32769)).toBe(false);
    expect(isInt16(32768)).toBe(false);
  });

  test("isUInt16 accepts 0..65535", () => {
    expect(isUInt16(0)).toBe(true);
    expect(isUInt16(65535)).toBe(true);
    expect(isUInt16(-1)).toBe(false);
    expect(isUInt16(65536)).toBe(false);
  });

  test("isInt32 accepts -2^31..2^31-1", () => {
    expect(isInt32(-2147483648)).toBe(true);
    expect(isInt32(2147483647)).toBe(true);
    expect(isInt32(0)).toBe(true);
    expect(isInt32(-2147483649)).toBe(false);
    expect(isInt32(2147483648)).toBe(false);
  });

  test("isUInt32 accepts 0..2^32-1", () => {
    expect(isUInt32(0)).toBe(true);
    expect(isUInt32(4294967295)).toBe(true);
    expect(isUInt32(-1)).toBe(false);
    expect(isUInt32(4294967296)).toBe(false);
  });

  test("isInt64 accepts safe integer range", () => {
    expect(isInt64(0)).toBe(true);
    expect(isInt64(Number.MAX_SAFE_INTEGER)).toBe(true);
    expect(isInt64(Number.MIN_SAFE_INTEGER)).toBe(true);
    expect(isInt64(1.5)).toBe(false);
  });

  test("isUInt64 accepts 0..safe integer range", () => {
    expect(isUInt64(0)).toBe(true);
    expect(isUInt64(Number.MAX_SAFE_INTEGER)).toBe(true);
    expect(isUInt64(-1)).toBe(false);
  });

  test("integer checks reject floats", () => {
    expect(isByte(1.5)).toBe(false);
    expect(isInt32(3.14)).toBe(false);
    expect(isInt64(0.1)).toBe(false);
  });

  test("integer checks reject non-numbers", () => {
    expect(isInt32("42")).toBe(false);
    expect(isInt32(null)).toBe(false);
    expect(isInt32(true)).toBe(false);
  });
});

describe("Floating-point type checks", () => {
  test("isFloat32 accepts any number", () => {
    expect(isFloat32(0)).toBe(true);
    expect(isFloat32(3.14)).toBe(true);
    expect(isFloat32(-Infinity)).toBe(true);
    expect(isFloat32(NaN)).toBe(true);
  });

  test("isFloat64 accepts any number", () => {
    expect(isFloat64(0)).toBe(true);
    expect(isFloat64(Number.MAX_VALUE)).toBe(true);
  });

  test("float checks reject non-numbers", () => {
    expect(isFloat32("3.14")).toBe(false);
    expect(isFloat64(null)).toBe(false);
  });
});

describe("Other primitive type checks", () => {
  test("isBool accepts booleans", () => {
    expect(isBool(true)).toBe(true);
    expect(isBool(false)).toBe(true);
    expect(isBool(0)).toBe(false);
    expect(isBool("true")).toBe(false);
  });

  test("isBigInt accepts bigints", () => {
    expect(isBigInt(0n)).toBe(true);
    expect(isBigInt(BigInt(42))).toBe(true);
    expect(isBigInt(42)).toBe(false);
    expect(isBigInt("42")).toBe(false);
  });
});

describe("Overload differentiation", () => {
  test("char vs string: single char matches both, but isChar is more specific", () => {
    const v = "a";
    expect(isChar(v)).toBe(true);
    expect(isString(v)).toBe(true);
    // Dispatcher should check isChar first (more specific)
  });

  test("byte vs int32: small number matches both, but isByte is more specific", () => {
    const v = 42;
    expect(isByte(v)).toBe(true);
    expect(isInt32(v)).toBe(true);
    // Dispatcher should check isByte first (more specific)
  });

  test("int32 vs float64: integer matches both", () => {
    expect(isInt32(42)).toBe(true);
    expect(isFloat64(42)).toBe(true);
    // Dispatcher should check isInt32 first (more specific)
  });

  test("int32 vs float64: float only matches float", () => {
    expect(isInt32(3.14)).toBe(false);
    expect(isFloat64(3.14)).toBe(true);
  });
});
