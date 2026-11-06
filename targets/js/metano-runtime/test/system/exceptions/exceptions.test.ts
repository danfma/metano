import { describe, expect, test } from "bun:test";
import {
  ArithmeticException,
  DivideByZeroException,
  Exception,
  OverflowException,
  SystemException,
} from "#/system/exceptions/index.ts";

/** Default messages and `ToString()` texts were printed by .NET 10.0.12. */
describe("exceptions", () => {
  const defaults: [string, () => Exception, string][] = [
    ["Exception", () => new Exception(), "Exception of type 'System.Exception' was thrown."],
    ["SystemException", () => new SystemException(), "System error."],
    [
      "ArithmeticException",
      () => new ArithmeticException(),
      "Overflow or underflow in the arithmetic operation.",
    ],
    ["OverflowException", () => new OverflowException(), "Arithmetic operation resulted in an overflow."],
    ["DivideByZeroException", () => new DivideByZeroException(), "Attempted to divide by zero."],
  ];

  test.each(defaults)("%s carries .NET's default message and its class name", (name, create, message) => {
    const exception = create();

    expect(exception.name).toBe(name);
    expect(exception.message).toBe(message);
    expect(exception.innerException).toBeNull();
    expect(exception.toString()).toBe(`System.${name}: ${message}`);
  });

  test("a null message falls back to the default, an empty one is kept", () => {
    expect(new Exception(null).message).toBe("Exception of type 'System.Exception' was thrown.");
    expect(new OverflowException("").message).toBe("");
    expect(new OverflowException("").toString()).toBe("System.OverflowException");
  });

  test("OverflowException derives from ArithmeticException, SystemException, Exception and Error", () => {
    const exception = new OverflowException("boom");

    expect(exception).toBeInstanceOf(ArithmeticException);
    expect(exception).toBeInstanceOf(SystemException);
    expect(exception).toBeInstanceOf(Exception);
    expect(exception).toBeInstanceOf(Error);
    expect(exception.toString()).toBe("System.OverflowException: boom");
  });

  test("the inner exception is also the standard cause and appears in toString as .NET writes it", () => {
    const inner = new ArithmeticException("inner");
    const outer = new OverflowException("outer", inner);

    expect(outer.innerException).toBe(inner);
    expect(outer.cause).toBe(inner);

    expect(outer.toString()).toBe(
      "System.OverflowException: outer\n ---> System.ArithmeticException: inner\n   --- End of inner exception stack trace ---",
    );
  });

  test("a plain Error can be the inner exception", () => {
    const inner = new TypeError("bad");
    const outer = new Exception("outer", inner);

    expect(outer.innerException).toBe(inner);
    expect(outer.toString()).toContain(" ---> TypeError: bad");
  });

  test("without an inner exception there is no cause", () => {
    expect("cause" in new Exception("x")).toBe(false);
  });

  test("stackTrace exposes the JavaScript stack", () => {
    const exception = new OverflowException();

    expect(exception.stackTrace).toBe(exception.stack ?? null);
    expect(exception.stackTrace).toContain("OverflowException");
  });
});
