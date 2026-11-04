import { describe, expect, test } from "bun:test";
import {
  type JsonConverter,
  JsonSerializer,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { expectJsonException } from "./json-exception-assertions.ts";

// `byte[]` as base64 (FR-046, slice S6b), measured against a source-generated
// System.Text.Json context on .NET 10 (SDK 10.0.401, runtime 10.0.12):
//   write []                → ""         [0]           → "AA=="
//         [1,2]             → "AQI="     [1,2,3]       → "AQID"
//         [255,254,253]     → "//79"     [251,255]     → "+/8="
//         0..99             → one line, padded with "==" (no line breaks)
//   read  "AQ\nID", "AQ ID", " AQID ", "AQ\r\nID", "AQ\tID" → [1,2,3]
//         "AQ= =", "AQ==\n", "\nAQ==" → [1]     "  " → []     null → null
//   throw "The JSON value could not be converted to System.Byte[]." for
//         "AQI", "AQ", "A", "AQI==", "AQ==AQ==", "AQ=", "A===", "====", "AR==", "AQJ=",
//         "-_8=", "A!ID", "=AQI", "AQ\fID", "AQ\u000bID", "AQ ID", "AQID\u0000",
//         [1,2], 1, true, {}

const bytesSpec: ValueSpec<number[] | null> = { descriptor: { kind: "bytes" } };

const CONVERSION_ERROR = "The JSON value could not be converted to System.Byte[].";

class Blob {
  constructor(
    readonly data: number[],
    readonly maybe: number[] | null,
  ) {}
}

const blobSpec: TypeSpec<Blob> = {
  type: Blob,
  factory: (p) => new Blob(p.data as number[], p.maybe as number[] | null),
  properties: [
    { ts: "data", json: "Data", type: { kind: "bytes" } },
    {
      ts: "maybe",
      json: "Maybe",
      type: { kind: "nullable", inner: { kind: "bytes" } },
      optional: true,
    },
  ],
};

const hundredBytes = Array.from({ length: 100 }, (_, index) => index);

const HUNDRED_BYTES_BASE64 =
  "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiYw==";

describe("bytes descriptor — write", () => {
  test.each([
    [[], ""],
    [[0], "AA=="],
    [[1, 2], "AQI="],
    [[1, 2, 3], "AQID"],
    [[255, 254, 253], "//79"],
    [[251, 255], "+/8="],
    [hundredBytes, HUNDRED_BYTES_BASE64],
  ])("%p → %p", (bytes, expected) => {
    expect(JsonSerializer.serialize(bytes, bytesSpec)).toBe(expected);
  });

  test("a null root writes null", () => {
    expect(JsonSerializer.serialize(null, bytesSpec)).toBeNull();
  });

  test("a property writes the base64 text, a null one null", () => {
    expect(JSON.stringify(JsonSerializer.serialize(new Blob([1, 2, 3], null), blobSpec))).toBe(
      '{"Data":"AQID","Maybe":null}',
    );
  });

  test("a Uint8Array writes like a number array", () => {
    expect(JsonSerializer.serialize(Uint8Array.of(1, 2, 3), bytesSpec as ValueSpec)).toBe("AQID");
  });

  test.each([[256], [-1], [1.5], [Number.NaN]])("rejects the non-byte %p", (value) => {
    expectJsonException(
      () => JsonSerializer.serialize([value], bytesSpec),
      `The value ${String(value)} is not a byte.`,
    );
  });
});

describe("bytes descriptor — read", () => {
  test.each([
    ["", []],
    ["AA==", [0]],
    ["AQI=", [1, 2]],
    ["AQID", [1, 2, 3]],
    ["//79", [255, 254, 253]],
    ["+/8=", [251, 255]],
    ["AQ\nID", [1, 2, 3]],
    ["AQ ID", [1, 2, 3]],
    [" AQID ", [1, 2, 3]],
    ["AQ\r\nID", [1, 2, 3]],
    ["AQ\tID", [1, 2, 3]],
    ["AQ= =", [1]],
    ["AQ==\n", [1]],
    ["\nAQ==", [1]],
    ["  ", []],
    [HUNDRED_BYTES_BASE64, hundredBytes],
  ])("%p → %p", (text, expected) => {
    expect(JsonSerializer.deserialize(text, bytesSpec)).toEqual(expected);
  });

  test("null reads as null", () => {
    expect(JsonSerializer.deserialize(null, bytesSpec)).toBeNull();
  });

  test.each([
    ["AQI"],
    ["AQ"],
    ["A"],
    ["AQI=="],
    ["AQ==AQ=="],
    ["AQ="],
    ["A==="],
    ["===="],
    ["AR=="],
    ["AQJ="],
    ["-_8="],
    ["A!ID"],
    ["=AQI"],
    ["AQ\fID"],
    ["AQ\u000bID"],
    ["AQ ID"],
    ["AQID\u0000"],
  ])("rejects the text %p", (text) => {
    expectJsonException(() => JsonSerializer.deserialize(text, bytesSpec), CONVERSION_ERROR);
  });

  test.each([[[1, 2]], [1], [true], [{}]])("rejects the JSON value %p", (value) => {
    expectJsonException(() => JsonSerializer.deserialize(value, bytesSpec), CONVERSION_ERROR);
  });

  test("a property and a nullable property read back", () => {
    const blob = JsonSerializer.deserialize(JSON.parse('{"Data":"AQID","Maybe":"BA=="}'), blobSpec);

    expect(blob).toEqual(new Blob([1, 2, 3], [4]));
  });

  test("a rejected property names System.Byte[]", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(JSON.parse('{"Data":[1],"Maybe":null}'), blobSpec),
      CONVERSION_ERROR,
    );
  });

  test("round-trips every byte value", () => {
    const allBytes = Array.from({ length: 256 }, (_, index) => index);
    const text = JsonSerializer.serialize(allBytes, bytesSpec);

    expect(JsonSerializer.deserialize(text, bytesSpec)).toEqual(allBytes);
  });
});

describe("bytes descriptor — custom converter", () => {
  test("a converter of kind bytes overrides the built-in one", () => {
    const hex: JsonConverter = {
      kind: "bytes",
      serialize: (value) => (value as number[]).map((byte) => byte.toString(16)).join(""),
      deserialize: (value) => [Number.parseInt(value as string, 16)],
    };

    expect(JsonSerializer.serialize([255], bytesSpec, [hex])).toBe("ff");
    expect(JsonSerializer.deserialize("ff", bytesSpec, [hex])).toEqual([255]);
  });
});
