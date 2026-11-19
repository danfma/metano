import { describe, expect, test } from "bun:test";

import {
  ArgumentNullException,
  Exception,
  InvalidOperationException,
  JsonException,
  KeyNotFoundException,
  ObjectDisposedException,
  OverflowException,
  SwitchExpressionException,
  SystemException,
  TaskCanceledException,
  TypeInitializationException,
} from "#/system/exceptions";

import type { ClassType } from "#/system/reflection";

/** The chain of `fullName`s from a descriptor up through its `base` thunks. */
function ancestry(type: ClassType<unknown>): string[] {
  const names = [type.fullName];
  let current = type.base?.();

  while (current !== undefined) {
    names.push(current.fullName);
    current = current.base?.();
  }

  return names;
}

describe("$runtimeType of the runtime exceptions", () => {
  test("describes the class as a class literal", () => {
    const type = InvalidOperationException.$runtimeType;

    expect(type.kind).toBe("class");
    expect(type.name).toBe("InvalidOperationException");
    expect(type.fullName).toBe("System.InvalidOperationException");
    expect(type.namespace).toBe("System");
    expect(type.ctor).toBe(InvalidOperationException);
    expect(type.interfaces).toEqual([]);
    expect(type.typeParameters).toEqual([]);
    expect(type.isAbstract).toBe(false);
    expect(type.isValueType).toBe(false);
  });

  test("is memoized", () => {
    expect(KeyNotFoundException.$runtimeType).toBe(KeyNotFoundException.$runtimeType);
  });

  test("is the class's own descriptor, never the inherited one", () => {
    expect(SystemException.$runtimeType).not.toBe(Exception.$runtimeType);
    expect(Object.hasOwn(OverflowException, "$runtimeType")).toBe(true);
  });

  test("links the .NET hierarchy through base thunks", () => {
    expect(ancestry(ArgumentNullException.$runtimeType)).toEqual([
      "System.ArgumentNullException",
      "System.ArgumentException",
      "System.SystemException",
      "System.Exception",
    ]);

    expect(ancestry(TaskCanceledException.$runtimeType)).toEqual([
      "System.Threading.Tasks.TaskCanceledException",
      "System.OperationCanceledException",
      "System.SystemException",
      "System.Exception",
    ]);

    expect(ancestry(JsonException.$runtimeType)).toEqual([
      "System.Text.Json.JsonException",
      "System.Exception",
    ]);
  });

  test("names each class in its .NET namespace", () => {
    expect(KeyNotFoundException.$runtimeType.namespace).toBe("System.Collections.Generic");

    expect(SwitchExpressionException.$runtimeType.fullName).toBe(
      "System.Runtime.CompilerServices.SwitchExpressionException",
    );
  });

  test("tests by instanceof", () => {
    const type = InvalidOperationException.$runtimeType;

    expect(type.test(new ObjectDisposedException("x"))).toBe(true);
    expect(type.test(new Exception())).toBe(false);
    expect(type.test(new Error("x"))).toBe(false);
  });

  test("creates through the parameterless constructor where .NET has one", () => {
    const created = InvalidOperationException.$runtimeType.create?.();

    expect(created).toBeInstanceOf(InvalidOperationException);

    expect(created?.message).toBe(
      "Operation is not valid due to the current state of the object.",
    );

    expect(ObjectDisposedException.$runtimeType.create).toBeUndefined();
    expect(TypeInitializationException.$runtimeType.create).toBeUndefined();
  });
});
