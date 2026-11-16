import { describe, expect, test } from "bun:test";
import { InvalidCastException } from "#/system/exceptions/index.ts";
import { castTo } from "#/system/reference-cast.ts";

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
