import { describe, expect, test } from "bun:test";

import { InlineCode } from "#/sample-runtime-types/interop/inline-code";

describe("Js.Code", () => {
  test("a typed expression reads the inline text's value", () => {
    expect(typeof InlineCode.now()).toBe("number");
  });

  test("a hole is spliced as an operand", () => {
    expect(InlineCode.sum(1.25, 2)).toBe("3.25");
    expect(InlineCode.shout("metano")).toBe("METANO");
  });

  test("statement text runs, and an untyped read sees its effect", () => {
    InlineCode.remember("kept");
    expect(InlineCode.remembered()).toBe("kept");
  });
});

describe("escape hatches", () => {
  test("UnsafeCast reinterprets parsed JSON without a check", () => {
    expect(InlineCode.parsePoint('{"x":1,"y":2}')).toEqual({ x: 1, y: 2 });
  });

  test("AsDynamic returns the same value", () => {
    const point = { x: 3, y: 4 };
    expect(InlineCode.loose(point)).toBe(point);
  });
});
