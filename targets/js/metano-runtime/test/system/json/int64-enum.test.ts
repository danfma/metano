import { describe, expect, test } from "bun:test";
import { JsonSerializer, type TypeDescriptor, type ValueSpec } from "#/system/json/index.ts";
import { JsonException } from "#/system/json/errors.ts";

// A 64-bit enum (`enum E : long` / `ulong`) holds `bigint` values (spec 011
// FR-044 / FR-051 as amended by #701): its numeric enum descriptor names the
// underlying type, so the converter writes and reads exact digits through the
// int64 path, then checks membership (#369). Expected texts were measured on
// .NET 10 (System.Text.Json): `{"W":1099511627777,"B":18446744073709551615}`,
// and a `Dictionary<B, int>` reads the key "9223372036854775809" exactly.

const Wide = { None: 0n, Low: 1n, High: 1099511627776n, Both: 1099511627777n } as const;

const Big = { Zero: 0n, Top: 9223372036854775808n, Max: 18446744073709551615n } as const;

const WIDE: TypeDescriptor = {
  kind: "numericEnum",
  values: Wide,
  typeName: "MyApp.Wide",
  numberType: "System.Int64",
};

const BIG: TypeDescriptor = {
  kind: "numericEnum",
  values: Big,
  typeName: "MyApp.Big",
  numberType: "System.UInt64",
};

function root(descriptor: TypeDescriptor): ValueSpec {
  return { descriptor, valueType: true };
}

describe("64-bit numeric enums", () => {
  test("write the exact digits of a bigint member", () => {
    expect(JSON.stringify(JsonSerializer.serialize(Wide.Both, root(WIDE)))).toBe(
      "1099511627777",
    );

    expect(JSON.stringify(JsonSerializer.serialize(Big.Max, root(BIG)))).toBe(
      "18446744073709551615",
    );
  });

  test("read a member beyond 2^53 exactly from the text", () => {
    expect(JsonSerializer.deserializeText("18446744073709551615", root(BIG))).toBe(Big.Max);
    expect(JsonSerializer.deserializeText("9223372036854775808", root(BIG))).toBe(Big.Top);
  });

  test("read a safe member from a parsed tree as a bigint", () => {
    expect(JsonSerializer.deserialize(1099511627777, root(WIDE))).toBe(Wide.Both);
  });

  test("reject an undeclared value, as every numeric enum does (#369)", () => {
    expect(() => JsonSerializer.deserializeText("7", root(WIDE))).toThrow(
      "Invalid numeric enum value: 7. Expected one of: 0, 1, 1099511627776, 1099511627777",
    );
  });

  test("reject a value outside the underlying type", () => {
    expect(() => JsonSerializer.deserializeText("18446744073709551616", root(BIG))).toThrow(
      JsonException,
    );

    expect(() => JsonSerializer.deserializeText("-1", root(BIG))).toThrow(JsonException);
  });

  test("read a dictionary key beyond 2^53 as its bigint member", () => {
    const spec: ValueSpec<Map<unknown, number> | null> = {
      descriptor: { kind: "map", key: BIG, value: { kind: "primitive" } },
    };

    const read = JsonSerializer.deserializeText('{"Max":1,"9223372036854775808":2}', spec);

    expect([...(read?.keys() ?? [])]).toEqual([Big.Max, Big.Top]);
    expect(JSON.stringify(JsonSerializer.serialize(read, spec))).toBe('{"Max":1,"Top":2}');
  });

  test("keep a 32-bit enum's number values", () => {
    const level: TypeDescriptor = { kind: "numericEnum", values: { Low: 0, High: 5 } };

    expect(JsonSerializer.deserializeText("5", root(level))).toBe(5);
  });
});
