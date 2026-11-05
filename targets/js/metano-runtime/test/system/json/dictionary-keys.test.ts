import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
import { Decimal } from "decimal.js";
import {
  type JsonConverter,
  JsonSerializer,
  SerializerContext,
  type TypeDescriptor,
  type ValueSpec,
} from "#/system/json/index.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";

// Dictionary keys (#479): every JSON property name is parsed into the key's
// C# type as System.Text.Json parses it, on the tree and the text paths.
// Expected values were measured on .NET 10 (System.Text.Json, Web defaults).

enum Level {
  Low = 0,
  High = 5,
}

const Flavor = { Sweet: "Sweet", Salty: "Salty" } as const;

const UUID = { create: (value: string) => value };

const GUID = "0f8fad5b-d9cb-469f-a165-70867728950e";

const LEVEL_TYPE_NAME = "MyApp.Level";

enum Alias {
  First = 1,
  Second = 1,
}

/** A `uint` enum with members past 2^31. */
enum Wide {
  Top = 2147483648,
  Max = 4294967295,
}

/** A `Dictionary<K, int>` spec. */
type IntValuedMapSpec = ValueSpec<Map<unknown, number> | null>;

/** A `Dictionary<K, int>` whose key descriptor is `key`. */
function mapSpec(key: TypeDescriptor): IntValuedMapSpec {
  return { descriptor: { kind: "map", key, value: { kind: "primitive" } } };
}

const INT32_KEY = mapSpec({ kind: "primitive", numberType: "System.Int32" });

const BYTE_LEVEL_KEY = mapSpec({
  kind: "numericEnum",
  values: Level,
  typeName: "MyApp.ByteLevel",
  numberType: "System.Byte",
});

const reads = {
  tree: (json: string, spec: IntValuedMapSpec) =>
    JsonSerializer.deserialize(JSON.parse(json), spec),
  text: (json: string, spec: IntValuedMapSpec) =>
    JsonSerializer.deserializeText(json, spec),
};

/** The keys a read of `name` gives, on the `path` read. */
function keysOf(
  path: keyof typeof reads,
  spec: IntValuedMapSpec,
  name: string,
): unknown[] {
  return [...(reads[path](JSON.stringify({ [name]: 1 }), spec)?.keys() ?? [])];
}

interface KeyReadRow {
  readonly label: string;
  readonly spec: IntValuedMapSpec;
  readonly name: string;
  readonly key: unknown;
}

interface KeyErrorRow {
  readonly label: string;
  readonly spec: IntValuedMapSpec;
  readonly name: string;
  readonly typeName: string;
}

const KEY_READS: readonly KeyReadRow[] = [
  { label: "int", spec: INT32_KEY, name: "-5", key: -5 },
  { label: "int with a plus sign", spec: INT32_KEY, name: "+5", key: 5 },
  { label: "int with leading zeros", spec: INT32_KEY, name: "007", key: 7 },
  { label: "int negative zero", spec: INT32_KEY, name: "-0", key: 0 },
  {
    label: "long within 2^53",
    spec: mapSpec({ kind: "primitive", numberType: "System.Int64" }),
    name: "-9007199254740991",
    key: -9007199254740991,
  },
  {
    label: "short",
    spec: mapSpec({ kind: "primitive", numberType: "System.Int16" }),
    name: "-3",
    key: -3,
  },
  {
    label: "byte",
    spec: mapSpec({ kind: "primitive", numberType: "System.Byte" }),
    name: "255",
    key: 255,
  },
  {
    label: "uint",
    spec: mapSpec({ kind: "primitive", numberType: "System.UInt32" }),
    name: "4294967295",
    key: 4294967295,
  },
  {
    label: "double",
    spec: mapSpec({ kind: "primitive", numberType: "System.Double" }),
    name: "1e2",
    key: 100,
  },
  {
    label: "double NaN",
    spec: mapSpec({ kind: "primitive", numberType: "System.Double" }),
    name: "NaN",
    key: Number.NaN,
  },
  {
    label: "bool in any case",
    spec: mapSpec({ kind: "primitive", typeName: "System.Boolean" }),
    name: "tRuE",
    key: true,
  },
  { label: "string", spec: mapSpec({ kind: "primitive" }), name: "007", key: "007" },
  {
    label: "numeric enum by name in any case",
    spec: mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME }),
    name: " high ",
    key: Level.High,
  },
  {
    label: "numeric enum by number",
    spec: mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME }),
    name: "+005",
    key: Level.High,
  },
  {
    label: "numeric enum by an undeclared number",
    spec: mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME }),
    name: "-1",
    key: -1,
  },
  {
    label: "numeric enum by a list of names",
    spec: mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME }),
    name: "High,Low",
    key: Level.High,
  },
  { label: "byte enum with a plus sign", spec: BYTE_LEVEL_KEY, name: "+5", key: Level.High },
  { label: "byte enum negative zero", spec: BYTE_LEVEL_KEY, name: "-0", key: Level.Low },
  {
    label: "uint enum at its maximum",
    spec: mapSpec({ kind: "numericEnum", values: Level, numberType: "System.UInt32" }),
    name: "4294967295",
    key: 4294967295,
  },
  {
    label: "uint enum member past 2^31 by name",
    spec: mapSpec({ kind: "numericEnum", values: Wide, numberType: "System.UInt32" }),
    name: "top",
    key: Wide.Top,
  },
  {
    label: "uint enum members past 2^31 by a list of names",
    spec: mapSpec({ kind: "numericEnum", values: Wide, numberType: "System.UInt32" }),
    name: "Top, Max",
    key: Wide.Max,
  },
  {
    label: "sbyte enum at its minimum",
    spec: mapSpec({ kind: "numericEnum", values: Level, numberType: "System.SByte" }),
    name: "-128",
    key: -128,
  },
  {
    label: "string enum in any case",
    spec: mapSpec({ kind: "enum", values: Flavor, typeName: "MyApp.Flavor" }),
    name: "salty",
    key: Flavor.Salty,
  },
  {
    label: "Guid in upper case",
    spec: mapSpec({ kind: "branded", create: UUID.create, typeName: "System.Guid" }),
    name: GUID.toUpperCase(),
    key: GUID,
  },
];

const KEY_ERRORS: readonly KeyErrorRow[] = [
  { label: "int", spec: INT32_KEY, name: "x", typeName: "System.Int32" },
  { label: "int with whitespace", spec: INT32_KEY, name: " 5", typeName: "System.Int32" },
  { label: "int with a fraction", spec: INT32_KEY, name: "1.0", typeName: "System.Int32" },
  { label: "int empty", spec: INT32_KEY, name: "", typeName: "System.Int32" },
  { label: "int out of range", spec: INT32_KEY, name: "2147483648", typeName: "System.Int32" },
  {
    label: "long out of range",
    spec: mapSpec({ kind: "primitive", numberType: "System.Int64" }),
    name: "9223372036854775808",
    typeName: "System.Int64",
  },
  {
    label: "short out of range",
    spec: mapSpec({ kind: "primitive", numberType: "System.Int16" }),
    name: "32768",
    typeName: "System.Int16",
  },
  {
    label: "byte with a sign",
    spec: mapSpec({ kind: "primitive", numberType: "System.Byte" }),
    name: "+5",
    typeName: "System.Byte",
  },
  {
    label: "uint negative",
    spec: mapSpec({ kind: "primitive", numberType: "System.UInt32" }),
    name: "-1",
    typeName: "System.UInt32",
  },
  {
    label: "double lower-case nan",
    spec: mapSpec({ kind: "primitive", numberType: "System.Double" }),
    name: "nan",
    typeName: "System.Double",
  },
  {
    label: "bool with whitespace",
    spec: mapSpec({ kind: "primitive", typeName: "System.Boolean" }),
    name: " true",
    typeName: "System.Boolean",
  },
  {
    label: "bool as a number",
    spec: mapSpec({ kind: "primitive", typeName: "System.Boolean" }),
    name: "1",
    typeName: "System.Boolean",
  },
  {
    label: "numeric enum unknown name",
    spec: mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME }),
    name: "x",
    typeName: LEVEL_TYPE_NAME,
  },
  {
    label: "numeric enum with a fraction",
    spec: mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME }),
    name: "5.0",
    typeName: LEVEL_TYPE_NAME,
  },
  {
    label: "byte enum out of range",
    spec: BYTE_LEVEL_KEY,
    name: "256",
    typeName: "MyApp.ByteLevel",
  },
  {
    label: "sbyte enum below its minimum",
    spec: mapSpec({
      kind: "numericEnum",
      values: Level,
      typeName: "MyApp.SmallLevel",
      numberType: "System.SByte",
    }),
    name: "-129",
    typeName: "MyApp.SmallLevel",
  },
  {
    label: "string enum unknown name",
    spec: mapSpec({ kind: "enum", values: Flavor, typeName: "MyApp.Flavor" }),
    name: "x",
    typeName: "MyApp.Flavor",
  },
  {
    label: "Guid in braces",
    spec: mapSpec({ kind: "branded", create: UUID.create, typeName: "System.Guid" }),
    name: `{${GUID}}`,
    typeName: "System.Guid",
  },
  {
    label: "Guid in the N format",
    spec: mapSpec({ kind: "branded", create: UUID.create, typeName: "System.Guid" }),
    name: GUID.replaceAll("-", ""),
    typeName: "System.Guid",
  },
  {
    label: "decimal",
    spec: mapSpec({ kind: "decimal", type: Decimal }),
    name: "NaN",
    typeName: "System.Decimal",
  },
  {
    label: "DateOnly",
    spec: mapSpec({
      kind: "temporal",
      parse: Temporal.PlainDate.from,
      typeName: "System.DateOnly",
    }),
    name: "x",
    typeName: "System.DateOnly",
  },
];

describe("dictionary keys — reads", () => {
  for (const path of ["tree", "text"] as const) {
    for (const row of KEY_READS) {
      test(`${path}: ${row.label} ${JSON.stringify(row.name)}`, () => {
        expect(keysOf(path, row.spec, row.name)).toEqual([row.key]);
      });
    }

    for (const row of KEY_ERRORS) {
      test(`${path}: ${row.label} ${JSON.stringify(row.name)} throws`, () => {
        expectJsonException(
          () => keysOf(path, row.spec, row.name),
          cannotConvertMessage(row.typeName),
        );
      });
    }
  }

  test("the context's read options do not change how a key reads", () => {
    const spec: IntValuedMapSpec = {
      ...INT32_KEY,
      readOptions: { numberHandling: "allowReadingFromString" },
    };

    expect(keysOf("tree", spec, "+5")).toEqual([5]);
  });

  test("a decimal key reads into the descriptor's type", () => {
    const [key] = keysOf("text", mapSpec({ kind: "decimal", type: Decimal }), "1e2");

    expect(key).toBeInstanceOf(Decimal);
    expect(String(key)).toBe("100");
  });

  test("a DateOnly key reads through the descriptor's parse", () => {
    const spec = mapSpec({
      kind: "temporal",
      parse: Temporal.PlainDate.from,
      typeName: "System.DateOnly",
    });

    const [key] = keysOf("tree", spec, "2024-01-02");

    expect(key).toBeInstanceOf(Temporal.PlainDate);
    expect(String(key)).toBe("2024-01-02");
  });

  test("two names that read as one key keep the one JS enumerates last", () => {
    expect([...(reads.text('{"1":1,"01":2}', INT32_KEY) ?? [])]).toEqual([[1, 2]]);

    // JS lists integer-like names first, so `"1"` comes before `"01"` here
    // too; .NET keeps the last name in the text (`2`).
    expect([...(reads.text('{"01":1,"1":2}', INT32_KEY) ?? [])]).toEqual([[1, 1]]);
  });

  test("a numeric enum key without a typeName still parses, and reports Object when it fails", () => {
    expect(keysOf("tree", mapSpec({ kind: "numericEnum", values: Level }), "High")).toEqual([
      Level.High,
    ]);

    expectJsonException(
      () => keysOf("tree", mapSpec({ kind: "numericEnum", values: Level }), "x"),
      cannotConvertMessage("Object"),
    );
  });
});

describe("dictionary keys — writes", () => {
  /** The JSON text a `Map` of `key` → `1` writes. */
  function writtenText(spec: IntValuedMapSpec, key: unknown): string {
    return JSON.stringify(JsonSerializer.serialize(new Map([[key, 1]]), spec));
  }

  test("numbers as String() writes them", () => {
    expect(writtenText(INT32_KEY, -5)).toBe('{"-5":1}');
  });

  test("bool as True / False", () => {
    const spec = mapSpec({ kind: "primitive", typeName: "System.Boolean" });

    expect(writtenText(spec, true)).toBe('{"True":1}');
    expect(writtenText(spec, false)).toBe('{"False":1}');
  });

  test("a numeric enum by member name, else by number", () => {
    const spec = mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME });

    expect(writtenText(spec, Level.High)).toBe('{"High":1}');
    expect(writtenText(spec, 7)).toBe('{"7":1}');
  });

  test("a decimal with its plain digits", () => {
    expect(writtenText(mapSpec({ kind: "decimal", type: Decimal }), new Decimal("1e-7"))).toBe(
      '{"0.0000001":1}',
    );
  });

  test("a DateOnly as its ISO text", () => {
    const spec = mapSpec({
      kind: "temporal",
      parse: Temporal.PlainDate.from,
      typeName: "System.DateOnly",
    });

    expect(writtenText(spec, Temporal.PlainDate.from("2024-01-02"))).toBe('{"2024-01-02":1}');
  });

  test("an aliased numeric enum value by its first member", () => {
    expect(writtenText(mapSpec({ kind: "numericEnum", values: Alias }), Alias.Second)).toBe(
      '{"First":1}',
    );
  });

  test("a written key reads back to the same key", () => {
    const spec = mapSpec({ kind: "numericEnum", values: Level, typeName: LEVEL_TYPE_NAME });
    const map = new Map([[Level.High, 1]]);

    expect(JsonSerializer.deserialize(JsonSerializer.serialize(map, spec), spec)).toEqual(map);
  });
});

describe("dictionary keys — custom converters", () => {
  test("a custom converter of the key's kind still reads and writes the key", () => {
    const upperCase: JsonConverter = {
      kind: "enum",
      serialize: (value) => String(value).toUpperCase(),
      deserialize: (value) => String(value).toLowerCase(),
    };

    const flavorKeys = mapSpec({ kind: "enum", values: Flavor, typeName: "MyApp.Flavor" });

    class KeyContext extends SerializerContext {
      constructor() {
        super({ converters: [upperCase] });
      }
    }

    const serializer = JsonSerializer.withContext(new KeyContext());

    expect(serializer.serialize(new Map([["ab", 1]]), flavorKeys)).toEqual({ AB: 1 });
    expect([...(serializer.deserialize({ AB: 1 }, flavorKeys)?.keys() ?? [])]).toEqual(["ab"]);
  });
});
