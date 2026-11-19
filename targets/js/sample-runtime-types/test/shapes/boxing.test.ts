import { describe, expect, test } from "bun:test";

import { getRuntimeType } from "metano-runtime";

import { Boxing } from "#/sample-runtime-types/shapes/boxing";
import { Color, Color$runtimeType } from "#/sample-runtime-types/shapes/color";

describe("boxing of enums (US3-9)", () => {
  test("a boxed enum answers its own type", () => {
    const boxed = Boxing.boxRed();

    expect(getRuntimeType(boxed)).toBe(Color$runtimeType);
    expect(Boxing.typeOf(boxed)).toBe(Color$runtimeType);
  });

  test("a boxed enum prints its member name", () => {
    expect(Boxing.textOf(Boxing.boxRed())).toBe("Red");
  });

  test("Equals boxes its argument and compares type and value", () => {
    expect(Boxing.isRed(Boxing.boxRed())).toBe(true);
    expect(Boxing.isRed(Color.Red)).toBe(false);
  });

  test("a type test on an object accepts a box or a raw value in range (FR-049, until 011c)", () => {
    expect(Boxing.isColor(Boxing.boxRed())).toBe(true);
    expect(Boxing.isColor(1)).toBe(true);
    expect(Boxing.isColor("Red")).toBe(false);
  });

  test("the elements of a List<Color> stay raw", () => {
    expect(Boxing.colors()).toEqual([Color.Red, Color.Green]);
  });
});
