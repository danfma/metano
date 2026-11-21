import { describe, expect, test } from "bun:test";

import { ArgumentException, getRuntimeTypeFromType, InvalidCastException } from "metano-runtime";

import { Circle } from "#/sample-runtime-types/shapes/circle";
import { Color, Color$runtimeType } from "#/sample-runtime-types/shapes/color";
import { IUnasked$runtimeType } from "#/sample-runtime-types/shapes/i-unasked";
import { Palette } from "#/sample-runtime-types/shapes/palette";
import { ShapeFactory } from "#/sample-runtime-types/shapes/shape-factory";
import { ShapeQuestions } from "#/sample-runtime-types/shapes/shape-questions";
import { TypedBox } from "#/sample-runtime-types/shapes/typed-box";

describe("runtime type descriptors (US3)", () => {
  test("a value made by a factory answers its own type", () => {
    expect(ShapeQuestions.nameOf("circle")).toBe("Circle");
    expect(ShapeQuestions.nameOf("square")).toBe("Square");
    expect(ShapeQuestions.isCircle("circle")).toBe(true);
    expect(ShapeQuestions.isCircle("square")).toBe(false);
  });

  test("an interface test is nominal for classes", () => {
    expect(ShapeQuestions.hasArea(ShapeFactory.create("circle"))).toBe(true);
    expect(ShapeQuestions.hasArea(ShapeFactory.create("square"))).toBe(false);
  });

  test("a structural look-alike is not an implementer", () => {
    expect(ShapeQuestions.hasArea({ area: 3 })).toBe(false);
  });

  test("a [PlainObject] implementer answers by its shape", () => {
    const dto = { area: 2, unit: "m2" };

    expect(ShapeQuestions.hasArea(dto)).toBe(true);
    expect(ShapeQuestions.areaOf(dto)).toBe(2);
  });

  test("a cast to a [PlainObject] checks its shape", () => {
    const dto = { area: 2, unit: "m2" };

    expect(ShapeQuestions.toAreaDto(dto)).toBe(dto);
    expect(() => ShapeQuestions.toAreaDto({ area: 2 })).toThrow(InvalidCastException);
  });

  test("[EmitRuntimeType] emits the descriptor of an interface nothing asks about", () => {
    expect(IUnasked$runtimeType.fullName).toBe("SampleRuntimeTypes.Shapes.IUnasked");
  });

  test("hand-written code reads a descriptor from a class or a descriptor", () => {
    expect(getRuntimeTypeFromType(Circle)).toBe(Circle.$runtimeType);
    expect(getRuntimeTypeFromType(Color$runtimeType)).toBe(Color$runtimeType);
    expect(() => getRuntimeTypeFromType(class {})).toThrow(ArgumentException);
  });
});

describe("enum helpers over the descriptor (FR-024)", () => {
  test("IsDefined answers the declared members", () => {
    expect(Palette.isDefined(Color.Green)).toBe(true);
    expect(Palette.isDefined(3)).toBe(false);
  });

  test("Parse reads names, flag combinations and numeric strings", () => {
    expect(Palette.parse("Red")).toBe(Color.Red);
    expect(Palette.parse("Red, Blue")).toBe(5);
    expect(Palette.parse("2")).toBe(Color.Green);
    expect(Palette.parseIgnoringCase("green")).toBe(Color.Green);
    expect(() => Palette.parse("Purple")).toThrow(ArgumentException);
  });

  test("ToObject keeps an undeclared value", () => {
    expect(Palette.fromRaw(8)).toBe(8);
  });

  test("US4 reified type parameters and generic class slots", () => {
    const circle = ShapeFactory.create("circle");
    expect(ShapeQuestions.isCircleViaGeneric(circle)).toBe(true);
    expect(ShapeQuestions.isCircleViaGeneric(ShapeFactory.create("square"))).toBe(false);

    const box = new TypedBox<Circle>([Circle.$runtimeType]);
    expect(box.matches(circle)).toBe(true);
    expect(box.matches(ShapeFactory.create("square"))).toBe(false);
  });
});
