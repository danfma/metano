import { describe, expect, test } from "bun:test";
import { JsonSerializer, type TypeSpec } from "#/system/json/index.ts";
import { expectJsonException } from "./json-exception-assertions.ts";

// `assignIfPresent` (FR-046), measured against a source-generated
// System.Text.Json context on .NET 10 (SDK 10.0.401, runtime 10.0.12):
//   class Init { string? Note = "init"; int Count = 5; List<int>? Items = [9] }  (set accessors)
//     {}                         → {"Note":"init","Count":5,"Items":[9]}
//     {"Count":1}                → {"Note":"init","Count":1,"Items":[9]}
//     {"Note":null,"Items":null} → {"Note":null,"Count":5,"Items":null}
//   record R(int A) { string? Note { get; init; } = "x" }   {"A":1} → {"A":1,"Note":null}
// An `init` member is always set (the generated factory assigns it), a
// `set` member only when the payload carries it.

class Init {
  note: string | null = "init";
  count = 5;
  items: number[] | null = [9];
}

const primitive = { kind: "primitive" } as const;

const initSpec: TypeSpec<Init> = {
  type: Init,
  factory: () => new Init(),
  properties: [
    {
      ts: "note",
      json: "Note",
      type: { kind: "nullable", inner: primitive },
      optional: true,
      assignIfPresent: true,
    },
    { ts: "count", json: "Count", type: primitive, assignIfPresent: true },
    {
      ts: "items",
      json: "Items",
      type: { kind: "nullable", inner: { kind: "array", element: primitive } },
      optional: true,
      assignIfPresent: true,
    },
  ],
};

function roundTrip(json: string): string {
  const value = JsonSerializer.deserialize(JSON.parse(json), initSpec);

  return JSON.stringify(JsonSerializer.serialize(value, initSpec));
}

describe("assignIfPresent", () => {
  test("an empty payload keeps every initializer", () => {
    expect(roundTrip("{}")).toBe('{"Note":"init","Count":5,"Items":[9]}');
  });

  test("a present member replaces its initializer, the others stay", () => {
    expect(roundTrip('{"Count":1}')).toBe('{"Note":"init","Count":1,"Items":[9]}');
  });

  test("an explicit null replaces a nullable initializer", () => {
    expect(roundTrip('{"Note":null,"Items":null}')).toBe('{"Note":null,"Count":5,"Items":null}');
  });

  test("the factory never sees a member set after construction", () => {
    const seen: Record<string, unknown>[] = [];
    const spec: TypeSpec<Init> = {
      ...initSpec,
      factory: (p) => {
        seen.push(p);
        return new Init();
      },
    };

    JsonSerializer.deserialize({ Count: 1 }, spec);

    expect(seen).toEqual([{ count: 1 }]);
  });

  test("a member without the flag is still required", () => {
    const strict: TypeSpec<Init> = {
      ...initSpec,
      properties: [{ ts: "count", json: "Count", type: primitive }],
    };

    expectJsonException(
      () => JsonSerializer.deserialize({}, strict),
      'Missing required property: "Count"',
    );
  });
});
