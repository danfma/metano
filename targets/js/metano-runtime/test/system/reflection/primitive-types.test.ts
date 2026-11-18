import { describe, expect, test } from "bun:test";
import {
  BigIntegerType,
  BooleanType,
  ByteType,
  CharType,
  DecimalType,
  DoubleType,
  GuidType,
  Int16Type,
  Int32Type,
  Int64Type,
  SByteType,
  SingleType,
  StringType,
  UInt16Type,
  UInt32Type,
  UInt64Type,
} from "#/system/reflection/primitive-types.ts";
import {
  DateOnlyType,
  DateTimeOffsetType,
  DateTimeType,
  TimeOnlyType,
  TimeSpanType,
} from "#/system/reflection/temporal-types.ts";
import { Temporal } from "@js-temporal/polyfill";
import Decimal from "decimal.js";

describe("primitive descriptors", () => {
  test("describe themselves as System primitives", () => {
    expect(Int32Type).toMatchObject({
      kind: "primitive",
      primitive: "Int32",
      name: "Int32",
      fullName: "System.Int32",
      namespace: "System",
    });
  });

  test("validate numbers by range", () => {
    expect(ByteType.test(255)).toBe(true);
    expect(ByteType.test(256)).toBe(false);
    expect(SByteType.test(-128)).toBe(true);
    expect(SByteType.test(128)).toBe(false);
    expect(Int16Type.test(32767)).toBe(true);
    expect(Int16Type.test(32768)).toBe(false);
    expect(UInt16Type.test(65535)).toBe(true);
    expect(UInt16Type.test(-1)).toBe(false);
    expect(Int32Type.test(2147483647)).toBe(true);
    expect(Int32Type.test(2147483648)).toBe(false);
    expect(Int32Type.test(1.5)).toBe(false);
    expect(UInt32Type.test(4294967295)).toBe(true);
    expect(UInt32Type.test(-1)).toBe(false);
    expect(SingleType.test(1.5)).toBe(true);
    expect(DoubleType.test(1.5)).toBe(true);
    expect(DoubleType.test("1")).toBe(false);
  });

  test("describe long, ulong and BigInteger as bigint", () => {
    expect(Int64Type.test(5n)).toBe(true);
    expect(Int64Type.test(5)).toBe(false);
    expect(Int64Type.test(2n ** 63n)).toBe(false);
    expect(UInt64Type.test(2n ** 63n)).toBe(true);
    expect(UInt64Type.test(-1n)).toBe(false);
    expect(BigIntegerType.test(2n ** 100n)).toBe(true);
    expect(BigIntegerType.test(1)).toBe(false);
  });

  test("validate booleans, strings, chars and guids", () => {
    expect(BooleanType.test(true)).toBe(true);
    expect(BooleanType.test(1)).toBe(false);
    expect(StringType.test("abc")).toBe(true);
    expect(StringType.test(1)).toBe(false);
    expect(CharType.test("a")).toBe(true);
    expect(CharType.test("ab")).toBe(false);
    expect(GuidType.test("2d9b9d1c-0000-4000-8000-000000000000")).toBe(true);
  });

  test("recognize decimal.js and Temporal values by tag", () => {
    expect(DecimalType.test(new Decimal("1.5"))).toBe(true);
    expect(DecimalType.test(1.5)).toBe(false);
    expect(DateTimeType.test(Temporal.PlainDateTime.from("2020-01-01T00:00:00"))).toBe(true);
    expect(DateTimeOffsetType.test(Temporal.ZonedDateTime.from("2020-01-01T00:00:00+00:00[UTC]"))).toBe(true);
    expect(DateOnlyType.test(Temporal.PlainDate.from("2020-01-01"))).toBe(true);
    expect(TimeOnlyType.test(Temporal.PlainTime.from("10:00:00"))).toBe(true);
    expect(TimeSpanType.test(Temporal.Duration.from({ hours: 1 }))).toBe(true);
    expect(DateOnlyType.test(Temporal.PlainTime.from("10:00:00"))).toBe(false);
    expect(DateOnlyType.test("2020-01-01")).toBe(false);
  });

  test("carry the .NET defaults", () => {
    expect(Int32Type.default?.()).toBe(0);
    expect(DoubleType.default?.()).toBe(0);
    expect(Int64Type.default?.()).toBe(0n);
    expect(BigIntegerType.default?.()).toBe(0n);
    expect(BooleanType.default?.()).toBe(false);
    expect(StringType.default?.()).toBe("");
    expect(CharType.default?.()).toBe("\0");
    expect(DecimalType.default).toBeUndefined();
    expect(GuidType.default).toBeUndefined();
    expect(DateOnlyType.default).toBeUndefined();
  });
});
