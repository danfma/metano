import {
  describe,
  expect,
  test,
} from "bun:test";

import { Stack } from "#/system/collections/stack.ts";
import { InvalidOperationException } from "#/system/exceptions/invalid-operation-exception.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

function thrown(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("Stack", () => {
  test("last in, first out; enumeration, toArray and JSON are top first", () => {
    const stack = Stack.from([1, 2, 3]);

    expect([...stack]).toEqual([3, 2, 1]);
    expect(stack.toArray()).toEqual([3, 2, 1]);
    expect(JSON.stringify(stack)).toBe("[3,2,1]");
    expect(stack.peek()).toBe(3);
    expect(stack.pop()).toBe(3);
    expect(stack.count).toBe(2);
  });

  test("empty pop / peek throw; try members return the default", () => {
    const stack = new Stack<string>();

    expect(thrown(() => stack.pop()).message).toBe("Stack empty.");
    expect(thrown(() => stack.peek()).message).toBe("Stack empty.");
    expect(stack.tryPop(null)).toEqual([false, null]);
    stack.push("a");
    expect(stack.tryPeek(null)).toEqual([true, "a"]);
    expect(stack.tryPop(null)).toEqual([true, "a"]);
  });

  test("its enumerator uses .NET's Stack message", () => {
    const stack = Stack.from([1, 2]);

    const error = thrown(() => {
      for (const _ of stack) stack.push(3);
    });

    expect(error).toBeInstanceOf(InvalidOperationException);
    expect(error.message).toBe("Collection was modified after the enumerator was instantiated.");
  });

  test("contains, copyTo (top first), clear, toString", () => {
    const stack = Stack.from([1, 2], [Int32Type]);
    const destination = [0, 0];

    stack.copyTo(destination);

    expect(destination).toEqual([2, 1]);
    expect(stack.contains(1)).toBe(true);
    expect(stack.toString()).toBe("System.Collections.Generic.Stack`1[System.Int32]");
    stack.clear();
    expect(stack.count).toBe(0);
  });
});
