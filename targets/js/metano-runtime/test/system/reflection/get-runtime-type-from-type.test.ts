import { describe, expect, test } from "bun:test";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { getRuntimeTypeFromType } from "#/system/reflection/get-runtime-type-from-type.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";
import { Circle, Stranger, Undescribed } from "./fixtures.ts";

describe("getRuntimeTypeFromType", () => {
  test("reads the own descriptor of a class", () => {
    expect(getRuntimeTypeFromType(Circle)).toBe(Circle.$runtimeType);
  });

  test("passes a descriptor through", () => {
    expect(getRuntimeTypeFromType(Int32Type)).toBe(Int32Type);
  });

  test("throws ArgumentException for a class without its own descriptor", () => {
    expect(() => getRuntimeTypeFromType(Undescribed)).toThrow(ArgumentException);
    expect(() => getRuntimeTypeFromType(Stranger)).toThrow(ArgumentException);
  });

  test("throws ArgumentException for anything else", () => {
    expect(() => getRuntimeTypeFromType(5)).toThrow(ArgumentException);
    expect(() => getRuntimeTypeFromType(null)).toThrow(ArgumentException);
    expect(() => getRuntimeTypeFromType({ kind: "x" })).toThrow(ArgumentException);
  });
});
