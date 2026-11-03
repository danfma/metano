import { describe, expect, test } from "bun:test";
import {
  JsonSerializer,
  collectProperties,
  collectWireProperties,
  type PropertySpec,
  type TypeSpec,
} from "#/system/json/index.ts";

// Mirrors the System.Text.Json probes of FR-009 (measured on .NET 10):
//   class Root { int Base; virtual string Name }
//   class Derived : Root { int X; override string Name }   → {"X":2,"Name":"d","Base":1}
//   class Mid : Derived { int Y; override string Name }    → {"Y":3,"Name":"d","X":2,"Base":1}
//   class Renamed : Root { int X; [JsonPropertyName("n")] override string Name }
//                                                          → {"X":2,"n":"d","Base":1,"Name":"d"}
//   class Ignored : Root { int X; [JsonIgnore] override string Name } → {"X":2,"Base":1}

class Root {
  constructor(
    readonly base: number,
    readonly name: string,
  ) {}
}

class Derived extends Root {
  constructor(
    base: number,
    name: string,
    readonly x: number,
  ) {
    super(base, name);
  }
}

class Mid extends Derived {
  constructor(
    base: number,
    name: string,
    x: number,
    readonly y: number,
  ) {
    super(base, name, x);
  }
}

const primitive = { kind: "primitive" } as const;

const rootSpec: TypeSpec<Root> = {
  type: Root,
  factory: (p) => new Root(p.base as number, p.name as string),
  properties: [
    { ts: "base", json: "Base", type: primitive },
    { ts: "name", json: "Name", type: primitive },
  ],
};

const derivedSpec: TypeSpec<Derived> = {
  type: Derived,
  base: rootSpec,
  factory: (p) => new Derived(p.base as number, p.name as string, p.x as number),
  properties: [
    { ts: "x", json: "X", type: primitive },
    { ts: "name", json: "Name", type: primitive },
  ],
};

const midSpec: TypeSpec<Mid> = {
  type: Mid,
  base: derivedSpec,
  factory: (p) => new Mid(p.base as number, p.name as string, p.x as number, p.y as number),
  properties: [
    { ts: "y", json: "Y", type: primitive },
    { ts: "name", json: "Name", type: primitive },
  ],
};

const renamedSpec: TypeSpec<Derived> = {
  type: Derived,
  base: rootSpec,
  factory: (p) => new Derived(p.base as number, p.name as string, p.x as number),
  properties: [
    { ts: "x", json: "X", type: primitive },
    { ts: "name", json: "n", type: primitive },
  ],
};

const ignoredSpec: TypeSpec<Derived> = {
  type: Derived,
  base: rootSpec,
  factory: (p) => new Derived(p.base as number, p.name as string, p.x as number),
  properties: [{ ts: "x", json: "X", type: primitive }],
  ignoredProperties: ["name"],
};

function jsonNamesOf(properties: readonly PropertySpec[]): string[] {
  return properties.map((property) => property.json);
}

describe("collectWireProperties", () => {
  test("lists own properties before the base's", () => {
    expect(jsonNamesOf(collectWireProperties(derivedSpec))).toEqual(["X", "Name", "Base"]);
  });

  test("keeps an override at its most-derived position, once", () => {
    expect(jsonNamesOf(collectWireProperties(midSpec))).toEqual(["Y", "Name", "X", "Base"]);
  });

  test("keeps the most-derived declaration of a JSON name", () => {
    const narrowed: TypeSpec = {
      base: rootSpec,
      factory: (p) => p,
      properties: [{ ts: "name", json: "Name", type: { kind: "nullable", inner: primitive } }],
    };

    const name = collectWireProperties(narrowed).find((property) => property.json === "Name");

    expect(name?.type.kind).toBe("nullable");
  });

  test("keeps a renamed override and the base name", () => {
    expect(jsonNamesOf(collectWireProperties(renamedSpec))).toEqual(["X", "n", "Base", "Name"]);
  });

  test("leaves out a base property an override ignores", () => {
    expect(jsonNamesOf(collectWireProperties(ignoredSpec))).toEqual(["X", "Base"]);
  });

  test("an ignored property stays hidden below a further base", () => {
    const leafSpec: TypeSpec = {
      base: ignoredSpec,
      factory: (p) => p,
      properties: [{ ts: "z", json: "Z", type: primitive }],
    };

    expect(jsonNamesOf(collectWireProperties(leafSpec))).toEqual(["Z", "X", "Base"]);
  });

  test("an override below the ignoring spec is written again", () => {
    const leafSpec: TypeSpec = {
      base: ignoredSpec,
      factory: (p) => p,
      properties: [{ ts: "name", json: "Name", type: primitive }],
    };

    expect(jsonNamesOf(collectWireProperties(leafSpec))).toEqual(["Name", "X", "Base"]);
  });

  test("collectProperties keeps its base-first contract", () => {
    expect(jsonNamesOf(collectProperties(derivedSpec))).toEqual(["Base", "Name", "X", "Name"]);
  });
});

describe("JsonSerializer — wire order", () => {
  test("writes a derived contract most-derived first", () => {
    const json = JsonSerializer.serialize(new Derived(1, "d", 2), derivedSpec);

    expect(JSON.stringify(json)).toBe('{"X":2,"Name":"d","Base":1}');
  });

  test("writes a two-level chain most-derived first", () => {
    const json = JsonSerializer.serialize(new Mid(1, "d", 2, 3), midSpec);

    expect(JSON.stringify(json)).toBe('{"Y":3,"Name":"d","X":2,"Base":1}');
  });

  test("writes a renamed override under both names", () => {
    const json = JsonSerializer.serialize(new Derived(1, "d", 2), renamedSpec);

    expect(JSON.stringify(json)).toBe('{"X":2,"n":"d","Base":1,"Name":"d"}');
  });

  test("an ignored override writes no base declaration", () => {
    const json = JsonSerializer.serialize(new Derived(1, "d", 2), ignoredSpec);

    expect(JSON.stringify(json)).toBe('{"X":2,"Base":1}');
  });

  test("an ignored override reads no base declaration", () => {
    const value = JsonSerializer.deserialize({ Name: "z", Base: 5, X: 7 }, ignoredSpec);

    expect(value.base).toBe(5);
    expect(value.x).toBe(7);
    expect(value.name).toBeUndefined();
  });

  test("an ignored override does not require the base key", () => {
    const value = JsonSerializer.deserialize({ Base: 5, X: 7 }, ignoredSpec);

    expect(value.x).toBe(7);
  });

  test("reads a derived contract in any key order", () => {
    const value = JsonSerializer.deserialize({ Base: 1, Name: "d", X: 2 }, derivedSpec);

    expect(value).toBeInstanceOf(Derived);
    expect(value).toEqual(new Derived(1, "d", 2));
  });
});
