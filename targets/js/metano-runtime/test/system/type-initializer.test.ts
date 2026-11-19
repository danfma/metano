import { describe, expect, test } from "bun:test";

import {
  Exception,
  InvalidOperationException,
  SystemException,
  TypeInitializationException,
} from "#/system/exceptions/index.ts";

import { typeInitializer } from "#/system/type-initializer.ts";

describe("typeInitializer", () => {
  test("runs the initializer once, at the first call", () => {
    const log: string[] = [];
    const init = typeInitializer("Registry", () => log.push("init"));

    log.push("before");
    init();
    init();

    expect(log).toEqual(["before", "init"]);
  });

  test("a re-entrant call sees the type as initialized", () => {
    const log: string[] = [];

    const init: () => void = typeInitializer("Registry", () => {
      log.push("start");
      init();
      log.push("end");
    });

    init();

    expect(log).toEqual(["start", "end"]);
  });

  test("wraps a failure and rethrows the same exception at every later call", () => {
    let runs = 0;
    const cause = new InvalidOperationException("boom");

    const init = typeInitializer("Outer.Inner", () => {
      runs++;

      throw cause;
    });

    const first = catchOf(init);
    const second = catchOf(init);

    expect(first).toBeInstanceOf(TypeInitializationException);
    expect(second).toBe(first);
    expect(runs).toBe(1);
    expect((first as TypeInitializationException).innerException).toBe(cause);
    expect((first as TypeInitializationException).cause).toBe(cause);
  });

  test("converts a thrown value that is not an Error", () => {
    const failure = catchOf(
      typeInitializer("Registry", () => {
        throw "plain";
      }),
    ) as TypeInitializationException;

    expect(failure.innerException).toBeInstanceOf(Exception);
    expect(failure.innerException?.message).toBe("plain");
  });
});

describe("TypeInitializationException", () => {
  test("carries .NET's message, the type name and the inner exception", () => {
    const inner = new InvalidOperationException("boom");
    const exception = new TypeInitializationException("Outer.Inner", inner);

    expect(exception).toBeInstanceOf(SystemException);
    expect(exception.name).toBe("TypeInitializationException");
    expect(exception.typeName).toBe("Outer.Inner");
    expect(exception.message).toBe("The type initializer for 'Outer.Inner' threw an exception.");
    expect(exception.innerException).toBe(inner);
  });
});

function catchOf(action: () => void): unknown {
  try {
    action();
  } catch (error) {
    return error;
  }

  return undefined;
}
