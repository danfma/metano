import { describe, expect, test } from "bun:test";
import { lazyValue } from "#/system/lazy-value";

describe("lazyValue", () => {
  test("builds nothing until the first read", () => {
    let builds = 0;

    lazyValue(() => ++builds);

    expect(builds).toBe(0);
  });

  test("builds once and returns the same value on every read", () => {
    let builds = 0;
    const read = lazyValue(() => ({ build: ++builds }));

    const first = read();

    expect(read()).toBe(first);
    expect(builds).toBe(1);
  });

  test("keeps an undefined value without building again", () => {
    let builds = 0;
    const read = lazyValue(() => {
      builds++;

      return undefined;
    });

    read();
    read();

    expect(builds).toBe(1);
  });
});
