import { describe, expect, test } from "bun:test";
import {
  JsonSerializer,
  type JsonConverter,
  type PropertySpec,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { expectJsonException } from "./json-exception-assertions.ts";
import { abstractFactory } from "./spec-fixtures.ts";

// #456: each spec is compiled once per custom-converter set into a plan (wire
// properties, resolved converters, read options, case-insensitive keys). A
// plan reads `spec.properties` exactly once, so counting those reads shows
// when a spec is compiled.

const primitive = { kind: "primitive" } as const;

/** `spec` with a `properties` getter that counts how often it is compiled. */
function counted<T>(spec: TypeSpec<T>): { readonly spec: TypeSpec<T>; compilations(): number } {
  const { properties, ...rest } = spec;
  const counter = { reads: 0 };

  const countedSpec: TypeSpec<T> = {
    ...rest,
    get properties(): readonly PropertySpec[] {
      counter.reads++;
      return properties;
    },
  };

  return { spec: countedSpec, compilations: () => counter.reads };
}

class Price {
  constructor(
    readonly label: string,
    readonly amount: number,
  ) {}
}

function priceSpecOf(): TypeSpec<Price> {
  return {
    type: Price,
    factory: (p) => new Price(p.label as string, p.amount as number),
    properties: [
      { ts: "label", json: "Label", type: primitive },
      { ts: "amount", json: "Amount", type: { kind: "decimal" } },
    ],
  };
}

/** Writes decimals as strings, reads them back from strings. */
const decimalAsText: JsonConverter = {
  kind: "decimal",
  serialize: (value) => String(value),
  deserialize: (value) => Number(value),
};

describe("Spec plans: reuse", () => {
  test("a spec is compiled once for any number of reads and writes", () => {
    const price = counted(priceSpecOf());

    for (let call = 0; call < 3; call++) {
      const json = JsonSerializer.serialize(new Price("a", 1), price.spec);
      JsonSerializer.deserialize(json, price.spec);
    }

    expect(price.compilations()).toBe(1);
  });

  test("a nested spec is compiled once for every element that reaches it", () => {
    const price = counted(priceSpecOf());

    const prices: ValueSpec<Price[]> = {
      descriptor: { kind: "array", element: { kind: "ref", spec: () => price.spec } },
    };

    const values = Array.from({ length: 10 }, (_, index) => new Price(`p${index}`, index));
    const json = JsonSerializer.serialize(values, prices);

    expect(JsonSerializer.deserialize(json, prices)).toEqual(values);
    expect(price.compilations()).toBe(1);
  });

  test("a value spec resolves its nested spec once", () => {
    const price = priceSpecOf();
    const specCalls = { count: 0 };

    const prices: ValueSpec<Price[]> = {
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => {
            specCalls.count++;
            return price;
          },
        },
      },
    };

    for (let call = 0; call < 3; call++) {
      JsonSerializer.serialize([new Price("a", 1), new Price("b", 2)], prices);
    }

    expect(specCalls.count).toBe(1);
  });
});

describe("Spec plans: custom converters per call", () => {
  test("each converter set gets its own plan, and the sets never leak into each other", () => {
    const price = counted(priceSpecOf());
    const value = new Price("a", 1.5);

    expect(JsonSerializer.serialize(value, price.spec)).toEqual({ Label: "a", Amount: 1.5 });

    expect(JsonSerializer.serialize(value, price.spec, [decimalAsText])).toEqual({
      Label: "a",
      Amount: "1.5",
    });

    expect(JsonSerializer.serialize(value, price.spec)).toEqual({ Label: "a", Amount: 1.5 });
    expect(price.compilations()).toBe(2);
  });

  test("a fresh array of the same converters reuses their plan", () => {
    const price = counted(priceSpecOf());

    for (let call = 0; call < 3; call++) {
      const json = JsonSerializer.serialize(new Price("a", 2), price.spec, [decimalAsText]);

      expect(JsonSerializer.deserialize(json, price.spec, [decimalAsText])).toEqual(
        new Price("a", 2),
      );
    }

    expect(price.compilations()).toBe(1);
  });

  test("no converters and an empty array share a plan", () => {
    const price = counted(priceSpecOf());

    JsonSerializer.serialize(new Price("a", 2), price.spec);
    JsonSerializer.serialize(new Price("a", 2), price.spec, []);

    expect(price.compilations()).toBe(1);
  });

  test("a converter array changed in place is honored on the next call", () => {
    const price = priceSpecOf();
    const converters: JsonConverter[] = [];

    expect(JsonSerializer.serialize(new Price("a", 3), price, converters)).toEqual({
      Label: "a",
      Amount: 3,
    });

    converters.push(decimalAsText);

    expect(JsonSerializer.serialize(new Price("a", 3), price, converters)).toEqual({
      Label: "a",
      Amount: "3",
    });
  });

  test("converters apply inside nested specs of the plan", () => {
    const price = priceSpecOf();
    const prices: ValueSpec<Price[]> = {
      descriptor: { kind: "array", element: { kind: "ref", spec: () => price } },
    };

    const json = JsonSerializer.serialize([new Price("a", 4)], prices, [decimalAsText]);

    expect(json).toEqual([{ Label: "a", Amount: "4" }]);
    expect(JsonSerializer.serialize([new Price("a", 4)], prices)).toEqual([
      { Label: "a", Amount: 4 },
    ]);
  });
});

// ─── Recursion ───────────────────────────────────────────────────────────────

class TreeNode {
  constructor(
    readonly name: string,
    readonly children: readonly TreeNode[],
  ) {}
}

const treeNodeSpec: TypeSpec<TreeNode> = {
  type: TreeNode,
  factory: (p) => new TreeNode(p.name as string, p.children as TreeNode[]),
  properties: [
    { ts: "name", json: "Name", type: primitive },
    {
      ts: "children",
      json: "Children",
      type: { kind: "array", element: { kind: "ref", spec: () => treeNodeSpec } },
    },
  ],
};

class Department {
  constructor(
    readonly name: string,
    readonly head: Employee | null,
  ) {}
}

class Employee {
  constructor(
    readonly name: string,
    readonly department: Department | null,
  ) {}
}

const departmentSpec: TypeSpec<Department> = {
  type: Department,
  factory: (p) => new Department(p.name as string, p.head as Employee | null),
  properties: [
    { ts: "name", json: "Name", type: primitive },
    { ts: "head", json: "Head", type: { kind: "ref", spec: () => employeeSpec }, optional: true },
  ],
};

const employeeSpec: TypeSpec<Employee> = {
  type: Employee,
  factory: (p) => new Employee(p.name as string, p.department as Department | null),
  properties: [
    { ts: "name", json: "Name", type: primitive },
    {
      ts: "department",
      json: "Department",
      type: { kind: "ref", spec: () => departmentSpec },
      optional: true,
    },
  ],
};

describe("Spec plans: recursive specs", () => {
  test("a self-recursive spec round-trips through its own plan", () => {
    const tree = new TreeNode("root", [
      new TreeNode("a", [new TreeNode("a1", [])]),
      new TreeNode("b", []),
    ]);

    const json = JsonSerializer.serialize(tree, treeNodeSpec);

    expect(json).toEqual({
      Name: "root",
      Children: [
        { Name: "a", Children: [{ Name: "a1", Children: [] }] },
        { Name: "b", Children: [] },
      ],
    });

    expect(JsonSerializer.deserialize(json, treeNodeSpec)).toEqual(tree);
  });

  test("mutually recursive specs round-trip in both directions", () => {
    const sales = new Department("Sales", new Employee("Ann", new Department("Inner", null)));
    const json = JsonSerializer.serialize(sales, departmentSpec);

    expect(json).toEqual({
      Name: "Sales",
      Head: { Name: "Ann", Department: { Name: "Inner", Head: null } },
    });

    expect(JsonSerializer.deserialize(json, departmentSpec)).toEqual(sales);

    const ann = new Employee("Ann", new Department("Sales", null));

    expect(
      JsonSerializer.deserialize(JsonSerializer.serialize(ann, employeeSpec), employeeSpec),
    ).toEqual(ann);
  });

  test("a recursive plan keeps the depth limit", () => {
    const deep = Array.from({ length: 70 }).reduce<TreeNode>(
      (child, _, index) => new TreeNode(`n${index}`, [child]),
      new TreeNode("leaf", []),
    );

    expectJsonException(
      () => JsonSerializer.serialize(deep, treeNodeSpec),
      "A possible object cycle was detected. This can either be due to a cycle or if the " +
        "object depth is larger than the maximum allowed depth of 64. Consider using " +
        "ReferenceHandler.Preserve on JsonSerializerOptions to support cycles.",
    );
  });
});

// ─── Read options ────────────────────────────────────────────────────────────

class Count {
  constructor(readonly total: number) {}
}

const countSpec: TypeSpec<Count> = {
  type: Count,
  factory: (p) => new Count(p.total as number),
  properties: [
    { ts: "total", json: "Total", type: { kind: "primitive", numberType: "System.Int32" } },
  ],
};

const lenientCountSpec: TypeSpec<Count> = {
  ...countSpec,
  readOptions: { propertyNameCaseInsensitive: true, numberHandling: "allowReadingFromString" },
};

describe("Spec plans: read options", () => {
  test("specs that differ only in read options keep their own plans", () => {
    for (let call = 0; call < 2; call++) {
      expect(JsonSerializer.deserialize({ total: "7" }, lenientCountSpec)).toEqual(new Count(7));

      expectJsonException(
        () => JsonSerializer.deserialize({ total: "7" }, countSpec),
        'Missing required property: "Total"',
      );

      expect(JsonSerializer.deserialize({ Total: 7 }, countSpec)).toEqual(new Count(7));
    }
  });

  test("a cached case-insensitive plan still lets the last matching key win", () => {
    for (let call = 0; call < 2; call++) {
      expect(JsonSerializer.deserialize({ TOTAL: 1, total: 2 }, lenientCountSpec)).toEqual(
        new Count(2),
      );
    }
  });

  test("a cached numbers-from-strings plan still rejects invalid text", () => {
    for (let call = 0; call < 2; call++) {
      expectJsonException(
        () => JsonSerializer.deserialize({ Total: "7.5" }, lenientCountSpec),
        "The JSON value could not be converted to System.Int32.",
      );
    }
  });
});

// ─── Polymorphism ────────────────────────────────────────────────────────────

abstract class Shape {}

class Square extends Shape {
  constructor(readonly side: number) {
    super();
  }
}

class Circle extends Shape {
  constructor(readonly radius: number) {
    super();
  }
}

/** A fresh `Shape` hierarchy, so each test counts its own compilations. */
function shapeSpecsOf() {
  const square = counted<Square>({
    type: Square,
    factory: (p) => new Square(p.side as number),
    properties: [{ ts: "side", json: "Side", type: primitive }],
  });

  const circleSpec: TypeSpec<Circle> = {
    type: Circle,
    factory: (p) => new Circle(p.radius as number),
    properties: [{ ts: "radius", json: "Radius", type: primitive }],
  };

  const shapeSpec: TypeSpec<Shape> = {
    type: Shape,
    factory: abstractFactory,
    properties: [],
    polymorphism: {
      discriminator: "$type",
      abstract: true,
      derived: [
        { tag: "square", spec: () => square.spec },
        { tag: "circle", spec: () => circleSpec },
      ],
    },
  };

  return { square, shapeSpec };
}

describe("Spec plans: polymorphism", () => {
  test("dispatch picks the derived contract per value, compiling each once", () => {
    const { square, shapeSpec } = shapeSpecsOf();
    const shapes = [new Square(1), new Circle(2), new Square(3)];

    for (let call = 0; call < 2; call++) {
      const json = shapes.map((shape) => JsonSerializer.serialize(shape, shapeSpec));

      expect(json).toEqual([
        { $type: "square", Side: 1 },
        { $type: "circle", Radius: 2 },
        { $type: "square", Side: 3 },
      ]);

      expect(json.map((item) => JsonSerializer.deserialize(item, shapeSpec))).toEqual(shapes);
    }

    expect(square.compilations()).toBe(1);
  });

  test("a derived contract shares its plan between dispatch and direct use", () => {
    const { square, shapeSpec } = shapeSpecsOf();

    expect(JsonSerializer.serialize(new Square(4), square.spec)).toEqual({ Side: 4 });
    expect(JsonSerializer.serialize(new Square(4), shapeSpec)).toEqual({
      $type: "square",
      Side: 4,
    });
    expect(JsonSerializer.deserialize({ Side: 4 }, square.spec)).toEqual(new Square(4));
    expect(JsonSerializer.deserialize({ $type: "square", Side: 4 }, shapeSpec)).toEqual(
      new Square(4),
    );
    expect(square.compilations()).toBe(1);
  });
});

// ─── Structural ([PlainObject]) polymorphism ─────────────────────────────────

interface Label {
  readonly text: string;
}

interface Point {
  readonly x: number;
}

interface Point3 extends Point {
  readonly z: number;
}

const isLabel = (value: unknown): value is Label =>
  typeof (value as Label | null)?.text === "string";

const isPoint = (value: unknown): value is Point => typeof (value as Point | null)?.x === "number";

const isPoint3 = (value: unknown): value is Point3 =>
  isPoint(value) && typeof (value as Point3).z === "number";

/** A fresh concrete `[PlainObject]` base with guarded variants, tested most specific first. */
function labelSpecsOf() {
  const point = counted<Point>({
    factory: (p) => ({ x: p.x as number }),
    properties: [{ ts: "x", json: "X", type: primitive }],
  });

  const point3 = counted<Point3>({
    factory: (p) => ({ x: p.x as number, z: p.z as number }),
    properties: [
      { ts: "z", json: "Z", type: primitive },
      { ts: "x", json: "X", type: primitive },
    ],
  });

  const label = counted<Label | Point>({
    factory: (p) => ({ text: p.text as string }),
    properties: [{ ts: "text", json: "Text", type: primitive }],
    polymorphism: {
      discriminator: "$type",
      typeName: "Label",
      derived: [
        { tag: "point", spec: () => point.spec as TypeSpec, test: isPoint },
        { tag: "point3", spec: () => point3.spec as TypeSpec, test: isPoint3 },
      ],
      testOrder: [1, 0],
      baseTest: isLabel,
    },
  });

  return { point, point3, label };
}

describe("Spec plans: structural polymorphism", () => {
  test("the guards select the target per value, each target compiled once", () => {
    const { point, point3, label } = labelSpecsOf();
    const values: (Label | Point)[] = [{ x: 1 }, { x: 2, z: 3 } as Point3, { text: "a" }, { x: 4 }];

    for (let call = 0; call < 2; call++) {
      const json = values.map((value) => JsonSerializer.serialize(value, label.spec));

      expect(json).toEqual([
        { $type: "point", X: 1 },
        { $type: "point3", Z: 3, X: 2 },
        { Text: "a" },
        { $type: "point", X: 4 },
      ]);

      expect(json.map((item) => JsonSerializer.deserialize(item, label.spec))).toEqual(values);
    }

    expect([point.compilations(), point3.compilations(), label.compilations()]).toEqual([1, 1, 1]);
  });

  test("a value no guard accepts still fails through the cached plan", () => {
    const { label } = labelSpecsOf();

    for (let call = 0; call < 2; call++) {
      expect(() =>
        JsonSerializer.serialize({ other: true } as unknown as Label, label.spec),
      ).toThrow("Runtime type 'Object' is not supported by polymorphic type 'Label'.");
    }
  });
});
