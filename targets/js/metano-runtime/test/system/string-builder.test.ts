import { describe, expect, test } from "bun:test";
import { ArgumentOutOfRangeException } from "#/system/exceptions/index.ts";
import { StringBuilder } from "#/system/string-builder.ts";

describe("StringBuilder", () => {
  test("appends text and chains", () => {
    const builder = new StringBuilder("a");

    expect(builder.append("b").appendLine("c").append(null).toString()).toBe("abc\n");
  });

  test("appends a repeated char", () => {
    expect(new StringBuilder().appendRepeated("z", 3).toString()).toBe("zzz");
    expect(() => new StringBuilder().appendRepeated("z", -1)).toThrow(ArgumentOutOfRangeException);
  });

  test("inserts, removes and replaces", () => {
    const builder = new StringBuilder("hello");

    builder.insert(0, ">").remove(1, 1).replace("l", "L");

    expect(builder.toString()).toBe(">eLLo");
  });

  test("rejects an index or count outside the text", () => {
    expect(() => new StringBuilder("ab").insert(3, "x")).toThrow(ArgumentOutOfRangeException);
    expect(() => new StringBuilder("ab").remove(1, 5)).toThrow(ArgumentOutOfRangeException);
    expect(() => new StringBuilder("ab").replace("", "x")).toThrow(ArgumentOutOfRangeException);
  });

  test("truncates and pads through length", () => {
    const builder = new StringBuilder("abc");

    builder.length = 1;
    expect(builder.toString()).toBe("a");

    builder.length = 3;
    expect(builder.toString()).toBe("a\0\0");
  });

  test("reads a part of the text", () => {
    expect(new StringBuilder("abcdef").toSubstring(2, 3)).toBe("cde");
  });

  test("clears", () => {
    expect(new StringBuilder("abc").clear().length).toBe(0);
  });
});
