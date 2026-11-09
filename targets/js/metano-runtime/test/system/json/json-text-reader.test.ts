import { describe, expect, test } from "bun:test";
import { JsonException } from "#/system/json";
import { JsonNonIntegerNumber, JsonNumberText } from "#/system/json/exact-json-number";
import {
  mayHoldInexactNumber,
  mayHoldNonIntegerNumber,
  mayHoldNumberTextLeaf,
  parseJsonText,
  readJsonText,
} from "#/system/json/json-text-reader";
import { expectedTextOutcomeOf, JSON_TEXT_ROWS, textOutcomeOf } from "./json-text-rows.ts";

/** The tree with every number leaf read as `JSON.parse` reads it. */
function asParsed(value: unknown): unknown {
  if (value instanceof JsonNumberText) {
    return Number(value.text);
  }

  if (value instanceof JsonNonIntegerNumber) {
    return value.value;
  }

  if (Array.isArray(value)) {
    return value.map(asParsed);
  }

  if (value !== null && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, asParsed(item)]));
  }

  return value;
}

function outcomeOf(read: () => unknown): string {
  try {
    return `OK ${JSON.stringify(asParsed(read()))}`;
  } catch (error) {
    const { name, message } = error as Error;

    return `${name} ${message}`;
  }
}

function jsonParseOutcomeOf(text: string): string {
  try {
    return `OK ${JSON.stringify(JSON.parse(text))}`;
  } catch {
    return "rejected";
  }
}

describe("readJsonText — System.Text.Json's reader messages (#457)", () => {
  for (const row of JSON_TEXT_ROWS) {
    const expected = row.error
      ? `JsonException ${row.error}`
      : `OK ${JSON.stringify(JSON.parse(row.text))}`;

    test(`${JSON.stringify(row.text)} → ${expected}`, () => {
      expect(outcomeOf(() => readJsonText(row.text))).toBe(expected);
    });
  }
});

describe("parseJsonText — JSON.parse with System.Text.Json's errors", () => {
  test("every row reads, or throws the reader's JsonException", () => {
    const outcomes = JSON_TEXT_ROWS.map((row) => textOutcomeOf(() => parseJsonText(row.text)));

    expect(outcomes).toEqual(JSON_TEXT_ROWS.map(expectedTextOutcomeOf));
  });

  test("the error is a JsonException, not the engine's SyntaxError", () => {
    expect(() => parseJsonText("{")).toThrow(JsonException);
  });
});

// FR-016's limit of 64 belongs to the contract walk, on every path; the
// tokenizer only bounds its own recursion.
describe("readJsonText — depth", () => {
  const nestedArrays = (depth: number) => `${"[".repeat(depth)}1${"]".repeat(depth)}`;
  const nestedObjects = (depth: number) => `${'{"a":'.repeat(depth)}1${"}".repeat(depth)}`;

  test("documents nested past 64 levels read as JSON.parse reads them", () => {
    for (const text of [nestedArrays(65), nestedObjects(65), nestedArrays(1000)]) {
      expect(asParsed(readJsonText(text))).toEqual(JSON.parse(text));
    }
  });

  test("past 1000 levels the tokenizer stops, short of the stack", () => {
    expect(() => readJsonText(nestedArrays(1001))).toThrow(
      "The JSON text nests containers deeper than 1000 levels, which deserializeText does not " +
        "read. Cannot read next JSON array.",
    );

    expect(() => readJsonText(nestedObjects(1001))).toThrow("Cannot read next JSON object.");
  });

  test("an unterminated document reports the open container", () => {
    expect(() => readJsonText("[".repeat(66))).toThrow("Expected depth to be zero");
  });
});

describe("readJsonText — a slash where a value or property opens", () => {
  // STJ's reader takes the `/` for a comment after a value (measured).
  for (const text of ["[/", "{/", '{"a":/', "[ /]", '{"a": /}']) {
    test(JSON.stringify(text), () => {
      expect(() => readJsonText(text)).toThrow(
        "'/' is invalid after a value. Expected either ',', '}', or ']'.",
      );
    });
  }

  test("after a comma it is an invalid start of a value", () => {
    expect(() => readJsonText("[1,/]")).toThrow("'/' is an invalid start of a value.");
  });
});

describe("readJsonText — the tree JSON.parse builds", () => {
  const sameAsJsonParse = (text: string) =>
    expect(asParsed(readJsonText(text))).toEqual(JSON.parse(text));

  test("the last duplicate key wins at the first key's position", () => {
    const text = '{"a":1,"b":2,"a":3}';

    expect(Object.entries(readJsonText(text) as object)).toEqual(Object.entries(JSON.parse(text)));
  });

  test("__proto__ is an own property, not the prototype", () => {
    const read = readJsonText('{"__proto__":{"polluted":true}}') as Record<string, unknown>;

    expect(Object.getPrototypeOf(read)).toBe(Object.prototype);
    expect(Object.keys(read)).toEqual(["__proto__"]);
    expect(({} as Record<string, unknown>).polluted).toBeUndefined();
  });

  test("integer-like keys are enumerated first, as JSON.parse enumerates them", () => {
    const text = '{"b":1,"2":2,"a":3,"1":4}';

    expect(Object.keys(readJsonText(text) as object)).toEqual(Object.keys(JSON.parse(text)));
  });

  // Spelled with escaped backslashes rather than String.raw: Bun's transpiler
  // rewrites a surrogate pair escape inside String.raw to `\u{…}`.
  test("escapes and surrogates", () => {
    sameAsJsonParse('["\\"\\\\\\/\\b\\f\\n\\r\\t", "\\u00e9\\uD83D\\uDE00", "é😀"]');
  });

  // STJ throws for both (an ArgumentException for the unescaped one, a
  // JsonException when the escaped one is read as a string); JSON.parse,
  // and so the runtime, accepts them.
  test("lone surrogates are accepted, as JSON.parse accepts them", () => {
    sameAsJsonParse('["\\uD800", "\\uDC00x"]');
    sameAsJsonParse('["\ud800"]');
  });

  test("numbers JSON.parse rounds to infinity, zero or -0 read the same", () => {
    sameAsJsonParse("[1e400, -1e400, 1e-400, -0, 0e5]");
    expect(Object.is(readJsonText("-0"), -0)).toBe(true);
  });

  test("a byte order mark is rejected, by JSON.parse and by STJ", () => {
    expect(() => readJsonText("﻿1")).toThrow("'0xEF' is an invalid start of a value.");
  });
});

describe("readJsonText — numbers beyond double precision", () => {
  const leafTextOf = (text: string) => {
    const read = readJsonText(text);

    return read instanceof JsonNumberText ? read.text : read;
  };

  test("a number with more than 15 significant digits keeps its text", () => {
    expect(leafTextOf("12345678901234567890.123456789")).toBe("12345678901234567890.123456789");
    expect(leafTextOf("-1234567890123456")).toBe("-1234567890123456");
    expect(leafTextOf("1.2345678901234567e5")).toBe("1.2345678901234567e5");
    expect(leafTextOf("1000000000000000000")).toBe("1000000000000000000");
  });

  // A decimal rounds a tie by its position in the text, which a double loses.
  test("leading fraction zeros count toward the 15 digits", () => {
    expect(leafTextOf("0.0000000000000025")).toBe("0.0000000000000025");
    expect(leafTextOf("0.000000000000025")).toBe(0.000000000000025);
    expect(leafTextOf("-0.000000000000000000001")).toBe("-0.000000000000000000001");
  });

  test("one with 15 digits or fewer is a number", () => {
    expect(leafTextOf("123456789012345")).toBe(123456789012345);
    expect(leafTextOf("-0.00000000001")).toBe(-1e-11);
    expect(leafTextOf("0.123456789012345e-5")).toBe(0.123456789012345e-5);
  });

  test("the leaves sit where JSON.parse puts the numbers", () => {
    const read = readJsonText('{"a":[1,12345678901234567890],"b":{"c":0.30000000000000004}}');

    expect(read).toEqual({
      a: [1, new JsonNumberText("12345678901234567890")],
      b: { c: new JsonNumberText("0.30000000000000004") },
    });
  });
});

describe("readJsonText — inexactOrNonInteger leaves (spec 012 FR-053)", () => {
  const leafTextOf = (text: string) => {
    const read = readJsonText(text, "inexactOrNonInteger");

    return read instanceof JsonNumberText ? read.text : read;
  };

  test("a short number with a fraction or an exponent is marked, holding its double", () => {
    expect(leafTextOf("1.0")).toEqual(new JsonNonIntegerNumber(1));
    expect(leafTextOf("1e3")).toEqual(new JsonNonIntegerNumber(1000));
    expect(leafTextOf("2E+0")).toEqual(new JsonNonIntegerNumber(2));
    expect(leafTextOf("1e400")).toEqual(new JsonNonIntegerNumber(Infinity));

    const negativeZero = leafTextOf("-0.0") as JsonNonIntegerNumber;

    expect(Object.is(negativeZero.value, -0)).toBe(true);
  });

  test("a long one keeps its text, as for a decimal", () => {
    expect(leafTextOf("1.0000000000000000")).toBe("1.0000000000000000");
    expect(leafTextOf("1152921504606846977e0")).toBe("1152921504606846977e0");
  });

  test("a short integer token is a number, and a long one keeps its digits", () => {
    expect(leafTextOf("42")).toBe(42);
    expect(Object.is(leafTextOf("-0"), -0)).toBe(true);
    expect(leafTextOf("9007199254740993")).toBe("9007199254740993");
  });

  test("the default leaves keep a short fraction a number", () => {
    expect(readJsonText("1.0")).toBe(1);
    expect(readJsonText("1e3")).toBe(1000);
  });

  test("the tree is JSON.parse's once the leaves are read as doubles", () => {
    const text = '{"a":[1,2.5,3e2],"b":{"c":-0.0,"d":"1.0"}}';

    expect(asParsed(readJsonText(text, "inexactOrNonInteger"))).toEqual(JSON.parse(text));
  });
});

/** `seed` with `character` inserted at `at`, the character at `at` deleted, or replaced. */
function mutate(seed: string, at: number, character: string, kind: number): string {
  switch (kind) {
    case 0:
      return seed.slice(0, at) + character + seed.slice(at);
    case 1:
      return seed.slice(0, at) + seed.slice(at + 1);
    default:
      return seed.slice(0, at) + character + seed.slice(at + 1);
  }
}

/** Seeded mutations of valid documents: `readJsonText` accepts exactly what `JSON.parse` accepts. */
describe("readJsonText — acceptance equals JSON.parse on mutated documents", () => {
  const SEEDS = [
    '{"id":1,"name":"Ana","tags":["a","b"],"price":19.99,"note":null,"ok":true}',
    '[{"a":[1,2,{"b":"\\u00e9\\n"}]},-0.5e-3,false,"x"]',
    '{"nested":{"deep":[[[]],{}]},"n":-12.5E+3,"s":"q\\"uote"}',
  ];

  const ALPHABET = ['"', "\\", "{", "}", "[", "]", ",", ":", "-", "+", ".", "e", "0", "1", " "];

  test("2000 mutations", () => {
    let state = 457;

    const random = (limit: number) => {
      state = (Math.imul(state, 1103515245) + 12345) & 0x7fffffff;

      return state % limit;
    };

    const mismatches: string[] = [];

    for (let round = 0; round < 2000; round++) {
      const seed = SEEDS[random(SEEDS.length)]!;
      const at = random(seed.length);
      const character = ALPHABET[random(ALPHABET.length)]!;
      const mutated = mutate(seed, at, character, random(3));

      const parsed = jsonParseOutcomeOf(mutated);
      const read = outcomeOf(() => readJsonText(mutated));
      const agrees = parsed === "rejected" ? read.startsWith("JsonException ") : read === parsed;

      if (!agrees) {
        mismatches.push(`${JSON.stringify(mutated)}: ${parsed} / ${read}`);
      }
    }

    expect(mismatches).toEqual([]);
  });
});

describe("mayHoldInexactNumber", () => {
  test("false when every number has at most 15 digits", () => {
    expect(mayHoldInexactNumber('{"a":123456789012345,"b":0.1234567890123,"c":"x"}')).toBe(false);
    expect(mayHoldInexactNumber("")).toBe(false);
  });

  test("true for a number with 16 digits, wherever it starts", () => {
    for (let offset = 0; offset < 40; offset++) {
      const text = `${" ".repeat(offset)}[1234567890123456]`;

      expect(mayHoldInexactNumber(text)).toBe(true);
    }
  });

  test("true for a 16-digit mantissa split by its decimal point or exponent", () => {
    expect(mayHoldInexactNumber("               1234567890.123456")).toBe(true);
    expect(mayHoldInexactNumber("               1234567890123456e-3")).toBe(true);
  });

  // A digit run inside a string, or a run of 16 characters with fewer
  // digits, only sends the text to the slower exact read.
  test("true for long digit runs that are not inexact numbers (conservative)", () => {
    expect(mayHoldInexactNumber('                "1234567890123456"')).toBe(true);
    expect(mayHoldInexactNumber("               1.23456789012345")).toBe(true);
  });
});

describe("mayHoldNonIntegerNumber / mayHoldNumberTextLeaf", () => {
  test("a digit before a point or an exponent marks a possible non-integer number", () => {
    expect(mayHoldNonIntegerNumber('{"a":1.0}')).toBe(true);
    expect(mayHoldNonIntegerNumber('{"a":1e3}')).toBe(true);
    expect(mayHoldNonIntegerNumber('{"a":1E3}')).toBe(true);
    expect(mayHoldNonIntegerNumber('{"a":[1,-20,300],"b":"text"}')).toBe(false);
  });

  // A string like a version or a GUID only sends the text to the slower exact read.
  test("true for strings that look like one (conservative)", () => {
    expect(mayHoldNonIntegerNumber('{"v":"1.0"}')).toBe(true);
    expect(mayHoldNonIntegerNumber('{"g":"3e4f0000"}')).toBe(true);
  });

  test("only inexactOrNonInteger leaves ask about non-integer numbers", () => {
    expect(mayHoldNumberTextLeaf('{"a":1.5}', "inexact")).toBe(false);
    expect(mayHoldNumberTextLeaf('{"a":1.5}', "inexactOrNonInteger")).toBe(true);
    expect(mayHoldNumberTextLeaf('{"a":1234567890123456}', "inexact")).toBe(true);
    expect(mayHoldNumberTextLeaf('{"a":12}', "inexactOrNonInteger")).toBe(false);
  });
});
