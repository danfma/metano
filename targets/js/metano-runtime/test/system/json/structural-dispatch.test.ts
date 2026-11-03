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

  test("a concrete [PlainObject] base writes an unmatched plain object as itself", () => {
    const concrete: TypeSpec<Shape> = {
      factory: (p) => ({ text: p.text as string }),
      properties: [{ ts: "text", json: "Text", type: primitive }],
      polymorphism: { discriminator: "$type", typeName: "Shape", derived: pointVariants },
    };

    expect(write(unmatched, concrete)).toBe('{"Text":"a"}');
    expect(read('{"Text":"a"}', concrete)).toEqual({ text: "a" });
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
