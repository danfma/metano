import { describe, expect, test } from "bun:test";
import { implementsInterface } from "#/system/reflection/implements-interface.ts";
import { Circle, FlatDto$rt, IDrawable$rt, IFlat$rt, IShape$rt, Stranger, Undescribed } from "./fixtures.ts";

describe("implementsInterface", () => {
  test("matches an interface listed by the class descriptor", () => {
    expect(implementsInterface(new Circle(), IShape$rt)).toBe(true);
  });

  test("matches an interface extended by a listed one", () => {
    expect(implementsInterface(new Circle(), IDrawable$rt)).toBe(true);
  });

  test("walks to the base class getter when the class has none of its own", () => {
    expect(implementsInterface(new Undescribed(), IShape$rt)).toBe(true);
  });

  test("rejects an undescribed class and an unrelated interface", () => {
    expect(implementsInterface(new Stranger(), IShape$rt)).toBe(false);
    expect(implementsInterface(new Circle(), IFlat$rt)).toBe(false);
  });

  test("matches a plain object through plainImplementers", () => {
    expect(implementsInterface({ flat: true }, IFlat$rt)).toBe(true);
    expect(implementsInterface({ other: 1 }, IFlat$rt)).toBe(false);
    expect(implementsInterface(Object.create(null, { flat: { value: true } }), IFlat$rt)).toBe(true);
  });

  test("never matches a primitive or null", () => {
    expect(implementsInterface(5, IShape$rt)).toBe(false);
    expect(implementsInterface("a", IFlat$rt)).toBe(false);
    expect(implementsInterface(null, IFlat$rt)).toBe(false);
    expect(FlatDto$rt.test({ flat: true })).toBe(true);
  });
});
