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
  SystemException,
  TaskCanceledException,
} from "#/system/exceptions/index.ts";
import { JsonException as JsonErrorsJsonException } from "#/system/json/errors.ts";

type ExceptionClass = abstract new (...args: never[]) => Exception;

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
    ["InvalidCastException", () => new InvalidCastException(), "System.InvalidCastException", "Specified cast is not valid."],
  ];

  test.each(defaults)("%s carries .NET's default message and full name", (name, create, fullName, message) => {
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
    ["ObjectDisposedException", new ObjectDisposedException(), [InvalidOperationException, SystemException]],
    ["TaskCanceledException", new TaskCanceledException(), [OperationCanceledException, SystemException]],
    ["DivideByZeroException", new DivideByZeroException(), [ArithmeticException, SystemException]],
    [
      "ArgumentOutOfRangeException",
      new ArgumentOutOfRangeException(),
      [ArgumentException, SystemException],
    ],
    ["JsonException", new JsonException(), []],
  ];

  test.each(ancestry)("%s is an Exception, an Error and each of its .NET ancestors", (_, exception, ancestors) => {
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
    expect(new ArgumentOutOfRangeException("p", 42, "range").message).toBe("range (Parameter 'p')\nActual value was 42.");
    expect(new ArgumentOutOfRangeException(null, 5, "m").message).toBe("m\nActual value was 5.");
    expect(new ArgumentOutOfRangeException("p", "", "m").message).toBe("m (Parameter 'p')\nActual value was .");
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
