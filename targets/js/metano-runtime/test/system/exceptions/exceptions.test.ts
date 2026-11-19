import { describe, expect, test } from "bun:test";

import {
  ArgumentException,
  ArgumentNullException,
  ArgumentOutOfRangeException,
  ArithmeticException,
  DivideByZeroException,
  Exception,
  FormatException,
  IndexOutOfRangeException,
  InvalidCastException,
  InvalidOperationException,
  JsonException,
  KeyNotFoundException,
  MissingMethodException,
  NotImplementedException,
  NotSupportedException,
  NullReferenceException,
  ObjectDisposedException,
  OperationCanceledException,
  OverflowException,
  SwitchExpressionException,
  SystemException,
  TaskCanceledException,
  TimeoutException,
} from "#/system/exceptions/index.ts";

import { JsonException as JsonErrorsJsonException } from "#/system/json/errors.ts";

type ExceptionClass = abstract new (...args: never[]) => Exception;

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
    [
      "OverflowException",
      () => new OverflowException(),
      "Arithmetic operation resulted in an overflow.",
    ],
    ["DivideByZeroException", () => new DivideByZeroException(), "Attempted to divide by zero."],
    [
      "ArgumentException",
      () => new ArgumentException(),
      "Value does not fall within the expected range.",
    ],
    ["ArgumentNullException", () => new ArgumentNullException(), "Value cannot be null."],
    [
      "FormatException",
      () => new FormatException(),
      "One of the identified items was in an invalid format.",
    ],
    [
      "InvalidOperationException",
      () => new InvalidOperationException(),
      "Operation is not valid due to the current state of the object.",
    ],
    [
      "NotSupportedException",
      () => new NotSupportedException(),
      "Specified method is not supported.",
    ],
  ];

  test.each(
    defaults,
  )("%s carries .NET's default message and its class name", (name, create, message) => {
    const exception = create();

    expect(exception.name).toBe(name);
    expect(exception.message).toBe(message);
    expect(exception.innerException).toBeNull();
    expect(exception.toString()).toBe(`System.${name}: ${message}`);
  });

  test("an argument exception appends its parameter name as .NET does", () => {
    const argument = new ArgumentException("'10' cannot be greater than 1.", "min");
    const argumentNull = new ArgumentNullException("s");

    expect(argument.message).toBe("'10' cannot be greater than 1. (Parameter 'min')");
    expect(argument.paramName).toBe("min");
    expect(new ArgumentException("bad").paramName).toBeNull();
    expect(argumentNull.message).toBe("Value cannot be null. (Parameter 's')");
    expect(argumentNull).toBeInstanceOf(ArgumentException);

    expect(argumentNull.toString()).toBe(
      "System.ArgumentNullException: Value cannot be null. (Parameter 's')",
    );
  });

  test("a null message falls back to the default, an empty one is kept", () => {
    expect(new Exception(null).message).toBe("Exception of type 'System.Exception' was thrown.");
    expect(new OverflowException("").message).toBe("");
    expect(new OverflowException("").toString()).toBe("System.OverflowException");
  });

  test(
    "OverflowException derives from ArithmeticException, SystemException, Exception and Error",
    () => {
      const exception = new OverflowException("boom");

      expect(exception).toBeInstanceOf(ArithmeticException);
      expect(exception).toBeInstanceOf(SystemException);
      expect(exception).toBeInstanceOf(Exception);
      expect(exception).toBeInstanceOf(Error);
      expect(exception.toString()).toBe("System.OverflowException: boom");
    },
  );

  test(
    "the inner exception is also the standard cause and appears in toString as .NET writes it",
    () => {
      const inner = new ArithmeticException("inner");
      const outer = new OverflowException("outer", inner);

      expect(outer.innerException).toBe(inner);
      expect(outer.cause).toBe(inner);

      expect(outer.toString()).toBe(
        "System.OverflowException: outer\n ---> System.ArithmeticException: inner\n   --- End of inner exception stack trace ---",
      );
    },
  );

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

/** Default messages and `ToString()` texts were printed by .NET 10. */
describe("exception hierarchy (spec 011 FR-013)", () => {
  const defaults: [string, () => Exception, string, string][] = [
    [
      "KeyNotFoundException",
      () => new KeyNotFoundException(),
      "System.Collections.Generic.KeyNotFoundException",
      "The given key was not present in the dictionary.",
    ],
    [
      "IndexOutOfRangeException",
      () => new IndexOutOfRangeException(),
      "System.IndexOutOfRangeException",
      "Index was outside the bounds of the array.",
    ],
    [
      "NotImplementedException",
      () => new NotImplementedException(),
      "System.NotImplementedException",
      "The method or operation is not implemented.",
    ],
    [
      "NullReferenceException",
      () => new NullReferenceException(),
      "System.NullReferenceException",
      "Object reference not set to an instance of an object.",
    ],
    [
      "MissingMethodException",
      () => new MissingMethodException(),
      "System.MissingMethodException",
      "Attempted to access a missing method.",
    ],
    [
      "OperationCanceledException",
      () => new OperationCanceledException(),
      "System.OperationCanceledException",
      "The operation was canceled.",
    ],
    [
      "TaskCanceledException",
      () => new TaskCanceledException(),
      "System.Threading.Tasks.TaskCanceledException",
      "A task was canceled.",
    ],
    [
      "JsonException",
      () => new JsonException(),
      "System.Text.Json.JsonException",
      "Exception of type 'System.Text.Json.JsonException' was thrown.",
    ],
    [
      "ArgumentOutOfRangeException",
      () => new ArgumentOutOfRangeException(),
      "System.ArgumentOutOfRangeException",
      "Specified argument was out of the range of valid values.",
    ],
    [
      "InvalidCastException",
      () => new InvalidCastException(),
      "System.InvalidCastException",
      "Specified cast is not valid.",
    ],
  ];

  test.each(
    defaults,
  )("%s carries .NET's default message and full name", (name, create, fullName, message) => {
    const exception = create();

    expect(exception.name).toBe(name);
    expect(exception.message).toBe(message);
    expect(exception.toString()).toBe(`${fullName}: ${message}`);
  });

  const ancestry: [string, Exception, ExceptionClass[]][] = [
    ["KeyNotFoundException", new KeyNotFoundException(), [SystemException]],
    ["IndexOutOfRangeException", new IndexOutOfRangeException(), [SystemException]],
    ["NotImplementedException", new NotImplementedException(), [SystemException]],
    ["NullReferenceException", new NullReferenceException(), [SystemException]],
    ["MissingMethodException", new MissingMethodException(), [SystemException]],
    [
      "ObjectDisposedException",
      new ObjectDisposedException(),
      [InvalidOperationException, SystemException],
    ],
    [
      "TaskCanceledException",
      new TaskCanceledException(),
      [OperationCanceledException, SystemException],
    ],
    ["DivideByZeroException", new DivideByZeroException(), [ArithmeticException, SystemException]],
    [
      "ArgumentOutOfRangeException",
      new ArgumentOutOfRangeException(),
      [ArgumentException, SystemException],
    ],
    ["JsonException", new JsonException(), []],
  ];

  test.each(
    ancestry,
  )("%s is an Exception, an Error and each of its .NET ancestors", (_, exception, ancestors) => {
    expect(exception).toBeInstanceOf(Exception);
    expect(exception).toBeInstanceOf(Error);

    for (const ancestor of ancestors) {
      expect(exception).toBeInstanceOf(ancestor);
    }
  });

  test("unrelated classes do not test as each other", () => {
    expect(new InvalidOperationException()).not.toBeInstanceOf(ArgumentException);
    expect(new FormatException()).not.toBeInstanceOf(OverflowException);
    expect(new JsonException()).not.toBeInstanceOf(SystemException);
    expect(new NotSupportedException()).not.toBeInstanceOf(InvalidOperationException);
  });

  test("the argument family takes an inner exception after its own roles", () => {
    const inner = new FormatException("f");

    expect(new ArgumentException("m", "p", inner).innerException).toBe(inner);
    expect(new ArgumentNullException("p", "m", inner).innerException).toBe(inner);
    expect(new ArgumentOutOfRangeException("p", null, "m", inner).innerException).toBe(inner);
  });

  test("an empty parameter name adds no suffix, as on .NET", () => {
    expect(new ArgumentException("m", "").message).toBe("m");
    expect(new ArgumentNullException("").message).toBe("Value cannot be null.");
  });

  test("an actual value is appended on a new line, unless it is null", () => {
    expect(new ArgumentOutOfRangeException("p", 42, "range").message).toBe(
      "range (Parameter 'p')\nActual value was 42.",
    );

    expect(new ArgumentOutOfRangeException(null, 5, "m").message).toBe("m\nActual value was 5.");

    expect(new ArgumentOutOfRangeException("p", "", "m").message).toBe(
      "m (Parameter 'p')\nActual value was .",
    );

    expect(new ArgumentOutOfRangeException("p", null, "m").message).toBe("m (Parameter 'p')");
    expect(new ArgumentOutOfRangeException("p", 42).actualValue).toBe(42);
  });

  test("an object name is appended on a new line, unless it is null or empty", () => {
    const disposed = new ObjectDisposedException("obj");

    expect(disposed.message).toBe("Cannot access a disposed object.\nObject name: 'obj'.");
    expect(disposed.objectName).toBe("obj");
    expect(new ObjectDisposedException("obj", "msg").message).toBe("msg\nObject name: 'obj'.");
    expect(new ObjectDisposedException("", "msg").message).toBe("msg");
    expect(new ObjectDisposedException(null, "msg").objectName).toBe("");
  });

  test("JsonException is the class the JSON serializer throws", () => {
    expect(JsonErrorsJsonException).toBe(JsonException);
    expect(new JsonException("bad").toString()).toBe("System.Text.Json.JsonException: bad");
  });
});

describe("TimeoutException and SwitchExpressionException", () => {
  test("TimeoutException is a SystemException with .NET's default message", () => {
    const exception = new TimeoutException();

    expect(exception).toBeInstanceOf(SystemException);
    expect(exception.toString()).toBe("System.TimeoutException: The operation has timed out.");
  });

  test("SwitchExpressionException names the unmatched value as .NET does", () => {
    const exception = new SwitchExpressionException(5);

    expect(exception).toBeInstanceOf(InvalidOperationException);

    expect(exception.message).toBe(
      "Non-exhaustive switch expression failed to match its input.\nUnmatched value was 5.",
    );

    expect(exception.unmatchedValue).toBe(5);

    expect(new SwitchExpressionException(null).message).toBe(
      "Non-exhaustive switch expression failed to match its input.",
    );

    expect(exception.toString()).toStartWith(
      "System.Runtime.CompilerServices.SwitchExpressionException: ",
    );
  });
});
