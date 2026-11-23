import { describe, expect, test } from "bun:test";
import { InvalidOperationException } from "#/system/exceptions/index.ts";
import { Lazy } from "#/system/lazy.ts";

describe("Lazy", () => {
  test("runs the factory on the first read, once", () => {
    let runs = 0;
    const lazy = new Lazy(() => ++runs);

    expect(runs).toBe(0);
    expect(lazy.isValueCreated).toBe(false);
    expect(lazy.value).toBe(1);
    expect(lazy.value).toBe(1);
    expect(lazy.isValueCreated).toBe(true);
  });

  test("throws the same exception on every read after a failure", () => {
    let runs = 0;
    const lazy = new Lazy<number>(() => {
      runs++;
      throw new Error("boom");
    });

    expect(() => lazy.value).toThrow("boom");
    expect(() => lazy.value).toThrow("boom");
    expect(runs).toBe(1);
    expect(lazy.isValueCreated).toBe(false);
  });

  test("throws InvalidOperationException when the factory reads its own value", () => {
    const lazy: Lazy<number> = new Lazy(() => lazy.value);

    expect(() => lazy.value).toThrow(InvalidOperationException);
  });
});
