import { describe, expect, test } from "bun:test";
import { namedException } from "#/system/exceptions/named-exception.ts";
import { Exception } from "#/system/exceptions/exception.ts";
import { NullReferenceException } from "#/system/exceptions/null-reference-exception.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";

import {
  BigIntegerType,
  BooleanType,
  DecimalType,
  DoubleType,
  Int32Type,
  Int64Type,
  StringType,
  UInt64Type,
} from "#/system/reflection/primitive-types.ts";

import {
  DateOnlyType,
  DateTimeOffsetType,
  DateTimeType,
  TimeOnlyType,
  TimeSpanType,
} from "#/system/reflection/temporal-types.ts";

import { ArrayType, DelegateType, ObjectType } from "#/system/reflection/well-known-types.ts";
import { specialize } from "#/system/reflection/specialize.ts";
import type { ClassType, RuntimeType } from "#/system/reflection/runtime-type.ts";
import { Temporal } from "@js-temporal/polyfill";
import Decimal from "decimal.js";
import { Circle, Stranger, Undescribed } from "./fixtures.ts";

describe("getRuntimeType", () => {
  test("throws NullReferenceException for null and undefined", () => {
    expect(() => getRuntimeType(null)).toThrow(NullReferenceException);

    expect(() => getRuntimeType(undefined)).toThrow(
      "Object reference not set to an instance of an object.",
    );
  });

  test("maps numbers, bigints, strings, booleans, functions and arrays", () => {
    expect(getRuntimeType(5)).toBe(Int32Type);
    expect(getRuntimeType(1.5)).toBe(DoubleType);
    expect(getRuntimeType(5n)).toBe(Int64Type);
    expect(getRuntimeType(-(2n ** 63n))).toBe(Int64Type);
    expect(getRuntimeType(2n ** 63n)).toBe(UInt64Type);
    expect(getRuntimeType(2n ** 64n)).toBe(BigIntegerType);
    expect(getRuntimeType(-(2n ** 63n) - 1n)).toBe(BigIntegerType);
    expect(getRuntimeType("a")).toBe(StringType);
    expect(getRuntimeType(true)).toBe(BooleanType);
    expect(getRuntimeType(() => 1)).toBe(DelegateType);
    expect(getRuntimeType([1])).toBe(ArrayType);
  });

  test("maps decimal.js and Temporal values by tag", () => {
    expect(getRuntimeType(new Decimal(1))).toBe(DecimalType);
    expect(getRuntimeType(Temporal.PlainDateTime.from("2020-01-01T00:00:00"))).toBe(DateTimeType);

    expect(getRuntimeType(Temporal.ZonedDateTime.from("2020-01-01T00:00:00+00:00[UTC]"))).toBe(
      DateTimeOffsetType,
    );

    expect(getRuntimeType(Temporal.PlainDate.from("2020-01-01"))).toBe(DateOnlyType);
    expect(getRuntimeType(Temporal.PlainTime.from("10:00:00"))).toBe(TimeOnlyType);
    expect(getRuntimeType(Temporal.Duration.from({ hours: 1 }))).toBe(TimeSpanType);
  });

  test("returns the own descriptor of the constructor", () => {
    expect(getRuntimeType(new Circle())).toBe(Circle.$runtimeType);
    expect(getRuntimeType(new Exception())).toBe(Exception.$runtimeType);
  });

  test("does not take a derived class without its own getter for its base", () => {
    const type = getRuntimeType(new Undescribed());

    expect(type).not.toBe(Circle.$runtimeType);
    expect(type.name).toBe("Undescribed");
    expect(getRuntimeType(namedException("My.Ns", "MyException")).name).toBe("NamedException");
  });

  test("maps plain objects to ObjectType", () => {
    expect(getRuntimeType({})).toBe(ObjectType);
    expect(getRuntimeType(Object.create(null))).toBe(ObjectType);
  });

  test("synthesizes one anonymous class descriptor per constructor", () => {
    const first = getRuntimeType(new Stranger());
    const second = getRuntimeType(new Stranger());

    expect(first).toBe(second);

    expect(first).toMatchObject({
      kind: "class",
      name: "Stranger",
      fullName: "Stranger",
      namespace: "",
    });

    expect(first.test(new Stranger())).toBe(true);
    expect(first.test({})).toBe(false);
  });

  test("maps an object whose constructor is not a function to ObjectType", () => {
    const value = Object.create({ constructor: 1 });

    expect(getRuntimeType(value)).toBe(ObjectType);
  });

  test("closes a reified generic class descriptor over its slot", () => {
    class Box<T> {
      constructor(typeArgs: readonly RuntimeType[]) {
        Object.defineProperty(this, "$typeArguments$Box", {
          value: typeArgs,
          enumerable: false,
        });
      }

      static get $runtimeType(): ClassType<Box<unknown>> {
        return Box$rt;
      }
    }

    const Box$rt: ClassType<Box<unknown>> = {
      kind: "class",
      name: "Box",
      fullName: "App.Box",
      namespace: "App",
      ctor: Box,
      interfaces: [],
      typeParameters: ["T"],
      slot: "$typeArguments$Box",
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Box<unknown> => v instanceof Box,
    };

    const intBox = new Box([Int32Type]);

    expect(getRuntimeType(intBox)).toBe(specialize(Box$rt, [Int32Type]));
    expect(getRuntimeType(intBox)).not.toBe(specialize(Box$rt, [StringType]));
  });
});
