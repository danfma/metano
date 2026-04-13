import { describe, expect, test } from "bun:test";
import { createDelegate, delegateAdd, delegateRemove, isDelegate } from "#/system/delegates.ts";

describe("isDelegate", () => {
  test("returns false for a plain function", () => {
    const fn = (x: number) => x * 2;
    expect(isDelegate(fn)).toBe(false);
  });

  test("returns false for null", () => {
    expect(isDelegate(null)).toBe(false);
  });

  test("returns true for a delegate created via createDelegate", () => {
    const fn = createDelegate((x: number) => x * 2);
    expect(isDelegate(fn)).toBe(true);
  });
});

describe("createDelegate", () => {
  test("calls all handlers and returns the last result", () => {
    const results: string[] = [];
    const d = createDelegate(
      (msg: string) => {
        results.push("A:" + msg);
        return 1;
      },
      (msg: string) => {
        results.push("B:" + msg);
        return 2;
      },
    );

    const ret = d("hello");
    expect(results).toEqual(["A:hello", "B:hello"]);
    expect(ret).toBe(2); // last handler's return value
  });
});

describe("delegateAdd", () => {
  test("first add on null returns the handler as-is (no wrapping)", () => {
    const handler = (x: number) => x;
    const result = delegateAdd(null, handler);

    expect(result).toBe(handler);
    expect(isDelegate(result)).toBe(false);
  });

  test("second add promotes to multicast", () => {
    const a = (x: number) => x;
    const b = (x: number) => x * 2;

    const result = delegateAdd(delegateAdd(null, a), b);
    expect(isDelegate(result)).toBe(true);
  });

  test("multicast delegate calls all handlers in order", () => {
    const log: number[] = [];
    const a = (x: number) => {
      log.push(x);
    };
    const b = (x: number) => {
      log.push(x * 10);
    };
    const c = (x: number) => {
      log.push(x * 100);
    };

    let d: typeof a | null = null;
    d = delegateAdd(d, a);
    d = delegateAdd(d, b);
    d = delegateAdd(d, c);
    d!(5);

    expect(log).toEqual([5, 50, 500]);
  });

  test("adding to an existing delegate mutates in place", () => {
    const a = () => {};
    const b = () => {};
    const c = () => {};

    let d = delegateAdd(delegateAdd(null as (() => void) | null, a), b);
    const ref = d;
    d = delegateAdd(d, c);

    expect(d).toBe(ref); // same reference — mutated in place
  });
});

describe("delegateRemove", () => {
  test("removing from null returns null", () => {
    const handler = () => {};
    expect(delegateRemove(null, handler)).toBeNull();
  });

  test("removing the only plain function returns null", () => {
    const handler = (x: number) => x;
    expect(delegateRemove(handler, handler)).toBeNull();
  });

  test("removing a non-matching plain function returns the original", () => {
    const a = (x: number) => x;
    const b = (x: number) => x * 2;
    expect(delegateRemove(a, b)).toBe(a);
  });

  test("removing last handler from multicast returns null", () => {
    const a = () => {};
    let d: (() => void) | null = delegateAdd(null, a);
    d = delegateRemove(d, a);

    expect(d).toBeNull();
  });

  test("removing down to one handler depromotes to plain function", () => {
    const a = () => {};
    const b = () => {};

    let d: (() => void) | null = null;
    d = delegateAdd(d, a);
    d = delegateAdd(d, b);
    expect(isDelegate(d)).toBe(true);

    d = delegateRemove(d, b);
    expect(d).toBe(a); // depromoted back to the original function
    expect(isDelegate(d)).toBe(false);
  });
});

describe("event simulation (full round-trip)", () => {
  test("simulates a C# event with add/remove/invoke", () => {
    const log: string[] = [];

    // Simulates: event EventHandler<string>? OnMessage;
    let onMessage: ((sender: unknown, msg: string) => void) | null = null;

    // += handler1
    const handler1 = (_: unknown, msg: string) => log.push("H1:" + msg);
    onMessage = delegateAdd(onMessage, handler1);

    // += handler2
    const handler2 = (_: unknown, msg: string) => log.push("H2:" + msg);
    onMessage = delegateAdd(onMessage, handler2);

    // Invoke
    onMessage?.(null, "hello");
    expect(log).toEqual(["H1:hello", "H2:hello"]);

    // -= handler1
    log.length = 0;
    onMessage = delegateRemove(onMessage, handler1);
    onMessage?.(null, "world");
    expect(log).toEqual(["H2:world"]);

    // -= handler2 (last one)
    onMessage = delegateRemove(onMessage, handler2);
    expect(onMessage).toBeNull();
  });
});
