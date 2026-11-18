import { describe, expect, test } from "bun:test";
import { InvalidCastException } from "#/system/exceptions/index.ts";
import { ArgumentException } from "#/system/exceptions/index.ts";
import { castTo } from "#/system/reflection/cast-to.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";

abstract class Shape {}
class Circle extends Shape {}
class Square extends Shape {}

/** The messages are the ones .NET 10 prints for the same casts. */
describe("castTo", () => {
  test("returns the same instance for a value of the type", () => {
    const circle = new Circle();

    expect(castTo<Circle>(circle, Circle)).toBe(circle);
  });

  test("accepts an instance of a subclass and of an abstract base", () => {
    const circle = new Circle();

    expect(castTo<Shape>(circle, Shape)).toBe(circle);
  });

  test("lets null and undefined through, as C# does for a null reference", () => {
    expect(castTo<Circle | null>(null, Circle)).toBeNull();
    expect(castTo<Circle | undefined>(undefined, Circle)).toBeUndefined();
  });

  test("throws InvalidCastException with the .NET message for another type", () => {
    const shape: Shape = new Square();

    expect(() => castTo<Circle>(shape, Circle)).toThrow(InvalidCastException);
    expect(() => castTo<Circle>(shape, Circle)).toThrow(
      "Unable to cast object of type 'Square' to type 'Circle'.",
    );
  });

  test("names a primitive value by its typeof", () => {
    expect(() => castTo<Circle>(5, Circle)).toThrow(
      "Unable to cast object of type 'number' to type 'Circle'.",
    );
  });

  test("tests a built-in class such as Error", () => {
    const error = new TypeError("boom");

    expect(castTo<Error>(error, Error)).toBe(error);
    expect(() => castTo<Error>({}, Error)).toThrow(InvalidCastException);
  });
});

describe("castTo with a runtime type descriptor", () => {
  test("returns the value when the descriptor's test passes", () => {
    expect(castTo<number>(5, Int32Type)).toBe(5);
  });

  test("lets null and undefined through", () => {
    expect(castTo<number | null>(null, Int32Type)).toBeNull();
    expect(castTo<number | undefined>(undefined, Int32Type)).toBeUndefined();
  });

  test("names a primitive by its descriptor and the target by its full name", () => {
    expect(() => castTo<number>("x", Int32Type)).toThrow(InvalidCastException);
    expect(() => castTo<number>("x", Int32Type)).toThrow(
      "Unable to cast object of type 'System.String' to type 'System.Int32'.",
    );
  });

  test("names a described class by its full name", () => {
    expect(() => castTo<string>(new ArgumentException("a"), StringType)).toThrow(
      "Unable to cast object of type 'System.ArgumentException' to type 'System.String'.",
    );
  });
});

describe("castTo with a described class", () => {
  test("names the target by its descriptor's full name", () => {
    expect(() => castTo<ArgumentException>(new Square(), ArgumentException)).toThrow(
      "Unable to cast object of type 'Square' to type 'System.ArgumentException'.",
    );
  });
});
