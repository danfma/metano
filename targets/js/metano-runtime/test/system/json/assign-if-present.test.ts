import { describe, expect, test } from "bun:test";
import { JsonSerializer, type TypeSpec } from "#/system/json/index.ts";
import { expectJsonException } from "./json-exception-assertions.ts";

// `TypeSpec.assignIfPresent` (FR-046), measured against a source-generated
// System.Text.Json context on .NET 10 (SDK 10.0.401, runtime 10.0.12):
//   class Init { string? Note = "init"; int Count = 5; List<int>? Items = [9] }  (set accessors)
//     {}                         → {"Note":"init","Count":5,"Items":[9]}
//     {"Count":1}                → {"Note":"init","Count":1,"Items":[9]}
//     {"Note":null,"Items":null} → {"Note":null,"Count":5,"Items":null}
//   record R(int A) { string? Note { get; init; } = "x" }   {"A":1} → {"A":1,"Note":null}
//   class BX { BX(int x) { X = x; } int X { get; set; } = 11 }
//   class DX : BX { DX() : base(0) {} int Y { get; set; } = 22 }
//     BX {"X":5} → {"X":5}; DX {"X":5,"Y":6} → {"Y":6,"X":5}; DX {} → {"Y":22,"X":0}
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
    { ts: "note", json: "Note", type: { kind: "nullable", inner: primitive }, optional: true },
    { ts: "count", json: "Count", type: primitive },
    {
      ts: "items",
      json: "Items",
      type: { kind: "nullable", inner: { kind: "array", element: primitive } },
      optional: true,
    },
  ],
  assignIfPresent: ["note", "count", "items"],
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

  test("a missing assignIfPresent member is left out of the factory's input", () => {
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

  test("a member outside the list is still required", () => {
    const strict: TypeSpec<Init> = { ...initSpec, assignIfPresent: undefined };

    expectJsonException(
      () => JsonSerializer.deserialize({}, strict),
      'Missing required property: "Count"',
    );
  });
});

class BX {
  x = 11;

  constructor(x: number) {
    this.x = x;
  }
}

class DX extends BX {
  y = 22;

  constructor() {
    super(0);
  }
}

const bxSpec: TypeSpec<BX> = {
  type: BX,
  factory: (p) => new BX(p.x as number),
  properties: [{ ts: "x", json: "X", type: primitive }],
};

const dxSpec: TypeSpec<DX> = {
  type: DX,
  base: bxSpec,
  factory: () => new DX(),
  properties: [{ ts: "y", json: "Y", type: primitive }],
  assignIfPresent: ["y", "x"],
};

describe("assignIfPresent is per contract", () => {
  test("the base constructor binds its member", () => {
    expect(JSON.stringify(JsonSerializer.serialize(JsonSerializer.deserialize({ X: 5 }, bxSpec), bxSpec))).toBe(
      '{"X":5}',
    );
  });

  test("a derived contract assigns the base member its own constructor does not bind", () => {
    const value = JsonSerializer.deserialize({ X: 5, Y: 6 }, dxSpec);

    expect(JSON.stringify(JsonSerializer.serialize(value, dxSpec))).toBe('{"Y":6,"X":5}');
  });

  test("a missing base member keeps the derived constructor's value", () => {
    const value = JsonSerializer.deserialize({}, dxSpec);

    expect(JSON.stringify(JsonSerializer.serialize(value, dxSpec))).toBe('{"Y":22,"X":0}');
  });

  test("a base set member the derived constructor binds is passed once, not assigned again", () => {
    let sets = 0;

    class Counted {
      private stored = 0;

      get v(): number {
        return this.stored;
      }

      set v(value: number) {
        sets += 1;
        this.stored = value;
      }
    }

    class Bound extends Counted {
      constructor(v: number) {
        super();
        this.v = v;
      }
    }

    const countedSpec: TypeSpec<Counted> = {
      type: Counted,
      factory: () => new Counted(),
      properties: [{ ts: "v", json: "V", type: primitive }],
      assignIfPresent: ["v"],
    };

    const boundSpec: TypeSpec<Bound> = {
      type: Bound,
      base: countedSpec,
      factory: (p) => new Bound(p.v as number),
      properties: [],
    };

    expect(JsonSerializer.deserialize({ V: 3 }, boundSpec).v).toBe(3);
    expect(sets).toBe(1);
  });
});
