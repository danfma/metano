import { beforeEach, describe, expect, test } from "bun:test";
import {
  JsonSerializer,
  type PolymorphismSpec,
  type TypeSpec,
  type UnknownDerivedTypeHandling,
} from "#/system/json/index.ts";
import {
  expectNotSupportedException,
  unsupportedRuntimeTypeMessage,
} from "./json-exception-assertions.ts";
import { abstractFactory } from "./spec-fixtures.ts";

// Hand-written specs shaped like contracts/context-codegen.md §1b: `[PlainObject]` variants have
// no constructor, so a write selects them with their synthesized guards (FR-019), in `testOrder`.
//   [PlainObject] abstract record Shape; Point(double X) "point"; Point3(X, Y, Z) : Point "point3"
//   Point3(1,2,3) → {"$type":"point3","Y":2,"Z":3,"X":1};  Point(1) → {"$type":"point","X":1}
//   {"$type":"point","X":1} reads as the plain object { x: 1 } (the tag, not the shape, selects).
// A plain object that matches no guard has no runtime type: STJ cannot produce one, so these rows
// are TS-only (E15 names it 'Object').

const primitive = { kind: "primitive" } as const;

interface Point {
  readonly x: number;
}

interface Point3 extends Point {
  readonly y: number;
  readonly z: number;
}

interface Label {
  readonly text: string;
}

type Shape = Point | Point3 | Label;

function isPoint(value: unknown): value is Point {
  return typeof (value as Point | null)?.x === "number";
}

function isPoint3(value: unknown): value is Point3 {
  const candidate = value as Point3 | null;

  return isPoint(value) && typeof candidate?.y === "number" && typeof candidate?.z === "number";
}

function isLabel(value: unknown): value is Label {
  return typeof (value as Label | null)?.text === "string";
}

const pointSpec: TypeSpec<Point> = {
  factory: (p) => ({ x: p.x as number }),
  properties: [{ ts: "x", json: "X", type: primitive }],
};

const point3Spec: TypeSpec<Point3> = {
  base: pointSpec,
  factory: (p) => ({ x: p.x as number, y: p.y as number, z: p.z as number }),
  properties: [
    { ts: "y", json: "Y", type: primitive },
    { ts: "z", json: "Z", type: primitive },
  ],
};

const pointVariants: PolymorphismSpec["derived"] = [
  { tag: "point", spec: () => pointSpec, test: isPoint },
  { tag: "point3", spec: () => point3Spec, test: isPoint3 },
];

function shapeBase(polymorphism: Partial<PolymorphismSpec>): TypeSpec<Shape> {
  return {
    factory: () => {
      throw new Error(
        "Cannot deserialize abstract type 'Shape' directly; deserialize it through a polymorphic spec.",
      );
    },
    properties: [],
    polymorphism: {
      discriminator: "$type",
      abstract: true,
      typeName: "Shape",
      derived: pointVariants,
      testOrder: [1, 0],
      ...polymorphism,
    },
  };
}

const plainShape = shapeBase({});

function write<T>(value: T, spec: TypeSpec<T>): string {
  return JSON.stringify(JsonSerializer.serialize(value, spec));
}

function read<T>(json: string, spec: TypeSpec<T>): T {
  return JsonSerializer.deserialize(JSON.parse(json), spec);
}

describe("structural dispatch — [PlainObject] variants (FR-019)", () => {
  test("a plain object is written as the variant whose guard accepts it (W26)", () => {
    expect(write<Shape>({ x: 1 }, plainShape)).toBe('{"$type":"point","X":1}');
  });

  test("testOrder tries the more specific variant first (W27)", () => {
    expect(write<Shape>({ x: 1, y: 2, z: 3 }, plainShape)).toBe(
      '{"$type":"point3","Y":2,"Z":3,"X":1}',
    );
  });

  test("without testOrder, guards run in attribute order", () => {
    const attributeOrder = shapeBase({ testOrder: undefined });

    expect(write<Shape>({ x: 1, y: 2, z: 3 }, attributeOrder)).toBe('{"$type":"point","X":1}');
  });

  test("the tag, not the shape, selects the variant on read", () => {
    const value = read('{"$type":"point","X":1}', plainShape);

    expect(value).toEqual({ x: 1 });
    expect(Object.getPrototypeOf(value)).toBe(Object.prototype);
  });

  test("a written point3 reads back as a plain point3", () => {
    expect(read(write<Shape>({ x: 1, y: 2, z: 3 }, plainShape), plainShape)).toEqual({
      x: 1,
      y: 2,
      z: 3,
    });
  });

  test("a null-prototype object is a plain object too", () => {
    const value = Object.assign(Object.create(null) as object, { x: 4 }) as Point;

    expect(write<Shape>(value, plainShape)).toBe('{"$type":"point","X":4}');
  });
});

describe("structural dispatch — unmatched plain objects", () => {
  const unmatched = { text: "a" } as unknown as Shape;

  test("failSerialization names the runtime type Object (E15)", () => {
    expectNotSupportedException(
      () => write(unmatched, plainShape),
      unsupportedRuntimeTypeMessage("Object", "Shape"),
    );
  });

  const fallbacks: readonly UnknownDerivedTypeHandling[] = [
    "fallBackToBaseType",
    "fallBackToNearestAncestor",
  ];

  for (const unknownDerivedType of fallbacks) {
    test(`${unknownDerivedType} writes the base contract with no tag`, () => {
      const base: TypeSpec<Shape> = {
        ...shapeBase({ unknownDerivedType }),
        properties: [{ ts: "text", json: "Text", type: primitive }],
      };

      expect(write(unmatched, base)).toBe('{"Text":"a"}');
    });
  }

  // FR-019 rule 5: a concrete [PlainObject] base `Label(string Text)` that does not register
  // itself writes its own values with no tag, as STJ writes a concrete `Animal("a")` through
  // `Animal` as `{"Name":"a"}` (measured, .NET 10).
  function concreteBase(overrides: Partial<PolymorphismSpec>): TypeSpec<Shape> {
    return {
      factory: (p) => ({ text: p.text as string }),
      properties: [{ ts: "text", json: "Text", type: primitive }],
      polymorphism: {
        discriminator: "$type",
        typeName: "Label",
        derived: pointVariants,
        testOrder: [1, 0],
        ...overrides,
      },
    };
  }

  test("a concrete [PlainObject] base writes a value its baseTest accepts as itself", () => {
    const concrete = concreteBase({ baseTest: isLabel });

    expect(write(unmatched, concrete)).toBe('{"Text":"a"}');
    expect(read('{"Text":"a"}', concrete)).toEqual({ text: "a" });
  });

  test("variant guards run before the baseTest", () => {
    const concrete = concreteBase({ baseTest: () => true });

    expect(write<Shape>({ x: 1, y: 2, z: 3 }, concrete)).toBe(
      '{"$type":"point3","Y":2,"Z":3,"X":1}',
    );
  });

  test("a value the baseTest rejects is an unregistered runtime type (Object)", () => {
    const concrete = concreteBase({ baseTest: isLabel });
    const stranger = { other: true } as unknown as Shape;

    expectNotSupportedException(
      () => write(stranger, concrete),
      unsupportedRuntimeTypeMessage("Object", "Label"),
    );
  });

  test("a value the baseTest rejects follows fallBackToBaseType", () => {
    const concrete = concreteBase({ baseTest: isLabel, unknownDerivedType: "fallBackToBaseType" });
    const stranger = { text: 1 } as unknown as Shape;

    expect(write(stranger, concrete)).toBe('{"Text":1}');
  });

  test("without a baseTest, an unmatched plain object is an unregistered runtime type", () => {
    expectNotSupportedException(
      () => write(unmatched, concreteBase({})),
      unsupportedRuntimeTypeMessage("Object", "Label"),
    );
  });

  test("a self-registered concrete [PlainObject] base writes its tag through its guard", () => {
    const selfRegistered: TypeSpec<Shape> = {
      factory: (p) => ({ text: p.text as string }),
      properties: [{ ts: "text", json: "Text", type: primitive }],
      polymorphism: {
        discriminator: "$type",
        typeName: "Shape",
        derived: [...pointVariants, { tag: "label", spec: () => selfRegistered, test: isLabel }],
      },
    };

    expect(write(unmatched, selfRegistered)).toBe('{"$type":"label","Text":"a"}');
    expect(read('{"$type":"label","Text":"a"}', selfRegistered)).toEqual({ text: "a" });
  });
});

// ─── Mixed class and [PlainObject] variants ──────────────────────────────────

abstract class Figure {}

class Circle extends Figure {
  constructor(readonly x: number) {
    super();
  }
}

class Square extends Figure {
  constructor(readonly x: number) {
    super();
  }
}

const circleSpec: TypeSpec<Circle> = {
  type: Circle,
  factory: (p) => new Circle(p.x as number),
  properties: [{ ts: "x", json: "X", type: primitive }],
};

describe("structural dispatch — mixed class and [PlainObject] variants", () => {
  const guardCalls: unknown[] = [];

  beforeEach(() => {
    guardCalls.length = 0;
  });

  const figureSpec: TypeSpec<Figure | Point> = {
    type: Figure,
    factory: abstractFactory,
    properties: [],
    polymorphism: {
      discriminator: "$type",
      abstract: true,
      derived: [
        { tag: "circle", spec: () => circleSpec },
        {
          tag: "point",
          spec: () => pointSpec,
          test: (value) => {
            guardCalls.push(value);

            return isPoint(value);
          },
        },
      ],
    },
  };

  test("a class instance is dispatched by constructor and never reaches a guard", () => {
    expect(write(new Circle(1), figureSpec)).toBe('{"$type":"circle","X":1}');
    expect(guardCalls).toEqual([]);
  });

  test("an unregistered class instance with a variant's shape still fails", () => {
    expectNotSupportedException(
      () => write(new Square(1), figureSpec),
      unsupportedRuntimeTypeMessage("Square", "Figure"),
    );

    expect(guardCalls).toEqual([]);
  });

  test("a plain object never matches a class variant by constructor", () => {
    expect(write<Figure | Point>({ x: 1 }, figureSpec)).toBe('{"$type":"point","X":1}');
  });

  test("reads dispatch both families by tag", () => {
    expect(read('{"$type":"circle","X":1}', figureSpec)).toBeInstanceOf(Circle);
    expect(read('{"$type":"point","X":1}', figureSpec)).toEqual({ x: 1 });
  });
});

// FR-019 rules 4–5: the variant guards run before `baseTest`, so a variant guard that accepts
// every base value captures them. Animal2(string Name) with Dog2(string Name, int? Bark): STJ
// writes Animal2("a") as {"Name":"a"} and Dog2("d", null) as {"$type":"dog","Bark":null,"Name":"d"}
// (measured, .NET 10). The generator reports MS0032 for such a hierarchy; this hand-written spec
// shows why.
describe("structural dispatch — a variant guard that accepts the base", () => {
  interface Animal2 {
    readonly name: string;
  }

  interface Dog2 extends Animal2 {
    readonly bark: number | null;
  }

  const isAnimal2 = (value: unknown): value is Animal2 =>
    typeof (value as Animal2 | null)?.name === "string";

  const isDog2 = (value: unknown): value is Dog2 => {
    const candidate = value as Dog2 | null;

    return isAnimal2(value) && (candidate?.bark == null || typeof candidate.bark === "number");
  };

  const animal2Spec: TypeSpec<Animal2> = {
    factory: (p) => ({ name: p.name as string }),
    properties: [{ ts: "name", json: "Name", type: primitive }],
    polymorphism: {
      discriminator: "$type",
      typeName: "Animal2",
      derived: [
        {
          tag: "dog",
          spec: () => dog2Spec,
          test: isDog2,
        },
      ],
      baseTest: isAnimal2,
    },
  };

  const dog2Spec: TypeSpec<Dog2> = {
    base: animal2Spec,
    factory: (p) => ({ name: p.name as string, bark: p.bark as number | null }),
    properties: [{ ts: "bark", json: "Bark", type: { kind: "nullable", inner: primitive } }],
  };

  test("a base value is written as the variant whose guard accepts it", () => {
    expect(write<Animal2>({ name: "a" }, animal2Spec)).toBe(
      '{"$type":"dog","Bark":null,"Name":"a"}',
    );
  });

  test("a variant value with the nullable member set is still written as the variant", () => {
    expect(write<Animal2>({ name: "d", bark: null } as Dog2, animal2Spec)).toBe(
      '{"$type":"dog","Bark":null,"Name":"d"}',
    );
  });
});
