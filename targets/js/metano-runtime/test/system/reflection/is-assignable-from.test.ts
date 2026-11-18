import { describe, expect, test } from "bun:test";
import { isAssignableFrom } from "#/system/reflection/is-assignable-from.ts";
import { nullableType } from "#/system/reflection/nullable-type.ts";
import { DoubleType, Int32Type } from "#/system/reflection/primitive-types.ts";
import { specialize } from "#/system/reflection/specialize.ts";
import { ObjectType, UnknownType } from "#/system/reflection/well-known-types.ts";
import { Circle, IDrawable$rt, IFlat$rt, IShape$rt, Shape } from "./fixtures.ts";

describe("isAssignableFrom", () => {
  test("is true for identity, unknown and object targets", () => {
    expect(isAssignableFrom(Int32Type, Int32Type)).toBe(true);
    expect(isAssignableFrom(UnknownType, Int32Type)).toBe(true);
    expect(isAssignableFrom(ObjectType, Circle.$runtimeType)).toBe(true);
    expect(isAssignableFrom(Int32Type, DoubleType)).toBe(false);
  });

  test("follows the class base chain", () => {
    expect(isAssignableFrom(Shape.$runtimeType, Circle.$runtimeType)).toBe(true);
    expect(isAssignableFrom(Circle.$runtimeType, Shape.$runtimeType)).toBe(false);
  });

  test("follows interfaces and their base interfaces", () => {
    expect(isAssignableFrom(IShape$rt, Circle.$runtimeType)).toBe(true);
    expect(isAssignableFrom(IDrawable$rt, Circle.$runtimeType)).toBe(true);
    expect(isAssignableFrom(IDrawable$rt, IShape$rt)).toBe(true);
    expect(isAssignableFrom(IShape$rt, IDrawable$rt)).toBe(false);
    expect(isAssignableFrom(IFlat$rt, Circle.$runtimeType)).toBe(false);
    expect(isAssignableFrom(IShape$rt, Int32Type)).toBe(false);
  });

  test("unwraps nullable", () => {
    expect(isAssignableFrom(nullableType(Int32Type), Int32Type)).toBe(true);
    expect(isAssignableFrom(nullableType(Int32Type), nullableType(Int32Type))).toBe(true);
    expect(isAssignableFrom(nullableType(Int32Type), DoubleType)).toBe(false);
    expect(isAssignableFrom(Int32Type, nullableType(Int32Type))).toBe(false);
  });

  test("compares specializations by their type arguments", () => {
    const open = Shape.$runtimeType;

    expect(isAssignableFrom(specialize(open, [Int32Type]), specialize(open, [Int32Type]))).toBe(true);
    expect(isAssignableFrom(specialize(open, [Int32Type]), specialize(open, [DoubleType]))).toBe(false);
  });
});
