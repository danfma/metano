import { describe, expect, test } from "bun:test";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { ArgumentNullException } from "#/system/exceptions/argument-null-exception.ts";
import { JsonSerializer, type TypeSpec } from "#/system/json/index.ts";
import { writeJsonText } from "#/system/json/json-text-writer.ts";

class Bag {
  constructor(
    readonly s: string,
    readonly d: number,
  ) {}
}

const bagSpec: TypeSpec<Bag> = {
  type: Bag,
  factory: (p) => new Bag(p.s as string, p.d as number),
  properties: [
    { ts: "s", json: "S", type: { kind: "primitive" } },
    { ts: "d", json: "D", type: { kind: "primitive" } },
  ],
};

// Expected texts measured on .NET 10 (`JsonSerializer.Serialize(value, ctx.Bag)`).
describe("writeJsonText", () => {
  test("escapes strings as JavaScriptEncoder.Default does", () => {
    const text = `a"b\\c\n\t\u0001<>&'+\`\u00e9\u20ac\u{1F600}\u007f/\b\f\r\u001f\u0080\u2028`;

    expect(writeJsonText(text)).toBe(
      String.raw`"a\u0022b\\c\n\t\u0001\u003C\u003E\u0026\u0027\u002B\u0060\u00E9\u20AC\uD83D\uDE00\u007F/\b\f\r\u001F\u0080\u2028"`,
    );
  });

  test("writes a lone surrogate as U+FFFD", () => {
    expect(writeJsonText("\ud800x\udc00")).toBe(String.raw`"\uFFFDx\uFFFD"`);
  });

  test("writes numbers in .NET's round-trip text", () => {
    expect(writeJsonText([1e20, 1e-7, 0.1, -0, 5e-324, 1e15, 1e16, 1.7976931348623157e308])).toBe(
      "[1E+20,1E-07,0.1,-0,5E-324,1000000000000000,10000000000000000,1.7976931348623157E+308]",
    );
  });

  test("throws ArgumentException for NaN and the infinities", () => {
    for (const value of [Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY]) {
      expect(() => writeJsonText({ d: value })).toThrow(ArgumentException);
    }
  });

  test("escapes property names and skips undefined members", () => {
    expect(writeJsonText({ "\u00e9<": 1, gone: undefined, n: null, list: [true, "x"] })).toBe(
      String.raw`{"\u00E9\u003C":1,"n":null,"list":[true,"x"]}`,
    );
  });
});

describe("JsonSerializer.serializeText", () => {
  test("writes a contract as STJ writes it", () => {
    expect(JsonSerializer.serializeText(new Bag("<\u00e9>", 1e20), bagSpec)).toBe(
      String.raw`{"S":"\u003C\u00E9\u003E","D":1E+20}`,
    );
  });

  test("writes a null root as null", () => {
    expect(JsonSerializer.serializeText(null, bagSpec)).toBe("null");
  });

  test("round-trips through deserializeText", () => {
    const text = JsonSerializer.serializeText(new Bag("a\"b", 0.5), bagSpec);

    expect(JsonSerializer.deserializeText(text, bagSpec)).toEqual(new Bag('a"b', 0.5));
  });

  test("throws ArgumentNullException for a null spec or text", () => {
    const missingSpec = null as unknown as TypeSpec<Bag>;
    const missingText = null as unknown as string;

    expect(() => JsonSerializer.serializeText(new Bag("x", 1), missingSpec)).toThrow(
      ArgumentNullException,
    );
    expect(() => JsonSerializer.deserializeText(missingText, bagSpec)).toThrow(
      ArgumentNullException,
    );
  });
});
