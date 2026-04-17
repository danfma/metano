import { describe, expect, test } from "bun:test";
import {
  JsonSerializer,
  SerializerContext,
  collectProperties,
  type TypeSpec,
  type JsonConverter,
} from "#/system/json/index.ts";
import { HashSet } from "#/system/collections/hash-set.ts";

// ─── Test Domain ─────────────────────────────────────────────────────────────

class Point {
  constructor(
    readonly x: number,
    readonly y: number,
  ) {}
}

const pointSpec: TypeSpec<Point> = {
  type: Point,
  factory: (p) => new Point(p.x as number, p.y as number),
  properties: [
    { ts: "x", json: "x", type: { kind: "primitive" } },
    { ts: "y", json: "y", type: { kind: "primitive" } },
  ],
};

class Person {
  constructor(
    readonly firstName: string,
    readonly age: number,
    readonly active: boolean,
  ) {}
}

const personSpec: TypeSpec<Person> = {
  type: Person,
  factory: (p) => new Person(p.firstName as string, p.age as number, p.active as boolean),
  properties: [
    { ts: "firstName", json: "first_name", type: { kind: "primitive" } },
    { ts: "age", json: "age", type: { kind: "primitive" } },
    { ts: "active", json: "is_active", type: { kind: "primitive" } },
  ],
};

// ─── Primitive Serialize/Deserialize ─────────────────────────────────────────

describe("JsonSerializer — primitives", () => {
  test("serialize simple object with pre-computed JSON names", () => {
    const person = new Person("Daniel", 30, true);
    const json = JsonSerializer.serialize(person, personSpec);

    expect(json).toEqual({
      first_name: "Daniel",
      age: 30,
      is_active: true,
    });
  });

  test("deserialize from JSON wire format to typed instance", () => {
    const data = { first_name: "Daniel", age: 30, is_active: true };
    const person = JsonSerializer.deserialize(data, personSpec);

    expect(person).toBeInstanceOf(Person);
    expect(person.firstName).toBe("Daniel");
    expect(person.age).toBe(30);
    expect(person.active).toBe(true);
  });

  test("round-trip preserves data", () => {
    const original = new Person("Alice", 25, false);
    const json = JsonSerializer.serialize(original, personSpec);
    const restored = JsonSerializer.deserialize(json, personSpec);

    expect(restored.firstName).toBe(original.firstName);
    expect(restored.age).toBe(original.age);
    expect(restored.active).toBe(original.active);
  });
});

// ─── Nullable ────────────────────────────────────────────────────────────────

describe("JsonSerializer — nullable", () => {
  class WithOptional {
    constructor(
      readonly name: string,
      readonly note: string | null,
    ) {}
  }

  const spec: TypeSpec<WithOptional> = {
    type: WithOptional,
    factory: (p) => new WithOptional(p.name as string, p.note as string | null),
    properties: [
      { ts: "name", json: "name", type: { kind: "primitive" } },
      {
        ts: "note",
        json: "note",
        type: { kind: "nullable", inner: { kind: "primitive" } },
        optional: true,
      },
    ],
  };

  test("serialize null value", () => {
    const obj = new WithOptional("test", null);
    const json = JsonSerializer.serialize(obj, spec);
    expect(json.note).toBeNull();
  });

  test("serialize non-null value", () => {
    const obj = new WithOptional("test", "hello");
    const json = JsonSerializer.serialize(obj, spec);
    expect(json.note).toBe("hello");
  });

  test("deserialize null", () => {
    const obj = JsonSerializer.deserialize({ name: "test", note: null }, spec);
    expect(obj.note).toBeNull();
  });

  test("deserialize missing optional", () => {
    const obj = JsonSerializer.deserialize({ name: "test" }, spec);
    expect(obj.note).toBeNull();
  });
});

// ─── Arrays ──────────────────────────────────────────────────────────────────

describe("JsonSerializer — arrays", () => {
  class WithTags {
    constructor(
      readonly name: string,
      readonly tags: string[],
    ) {}
  }

  const spec: TypeSpec<WithTags> = {
    type: WithTags,
    factory: (p) => new WithTags(p.name as string, p.tags as string[]),
    properties: [
      { ts: "name", json: "name", type: { kind: "primitive" } },
      { ts: "tags", json: "tags", type: { kind: "array", element: { kind: "primitive" } } },
    ],
  };

  test("serialize array of primitives", () => {
    const obj = new WithTags("item", ["a", "b", "c"]);
    const json = JsonSerializer.serialize(obj, spec);
    expect(json.tags).toEqual(["a", "b", "c"]);
  });

  test("deserialize array of primitives", () => {
    const obj = JsonSerializer.deserialize({ name: "item", tags: ["a", "b"] }, spec);
    expect(obj.tags).toEqual(["a", "b"]);
  });
});

// ─── Nested Ref (array of objects) ───────────────────────────────────────────

describe("JsonSerializer — ref (nested objects)", () => {
  class Line {
    constructor(
      readonly start: Point,
      readonly end: Point,
    ) {}
  }

  const lineSpec: TypeSpec<Line> = {
    type: Line,
    factory: (p) => new Line(p.start as Point, p.end as Point),
    properties: [
      { ts: "start", json: "start", type: { kind: "ref", spec: () => pointSpec } },
      { ts: "end", json: "end", type: { kind: "ref", spec: () => pointSpec } },
    ],
  };

  test("serialize nested objects", () => {
    const line = new Line(new Point(1, 2), new Point(3, 4));
    const json = JsonSerializer.serialize(line, lineSpec);

    expect(json).toEqual({
      start: { x: 1, y: 2 },
      end: { x: 3, y: 4 },
    });
  });

  test("deserialize nested objects", () => {
    const data = { start: { x: 1, y: 2 }, end: { x: 3, y: 4 } };
    const line = JsonSerializer.deserialize(data, lineSpec);

    expect(line).toBeInstanceOf(Line);
    expect(line.start).toBeInstanceOf(Point);
    expect(line.start.x).toBe(1);
    expect(line.end.y).toBe(4);
  });

  test("array of refs", () => {
    class Shape {
      constructor(readonly points: Point[]) {}
    }

    const shapeSpec: TypeSpec<Shape> = {
      type: Shape,
      factory: (p) => new Shape(p.points as Point[]),
      properties: [
        {
          ts: "points",
          json: "points",
          type: { kind: "array", element: { kind: "ref", spec: () => pointSpec } },
        },
      ],
    };

    const shape = new Shape([new Point(0, 0), new Point(1, 1)]);
    const json = JsonSerializer.serialize(shape, shapeSpec);
    expect(json.points).toEqual([
      { x: 0, y: 0 },
      { x: 1, y: 1 },
    ]);

    const restored = JsonSerializer.deserialize(json, shapeSpec);
    expect(restored.points[0]).toBeInstanceOf(Point);
    expect(restored.points[1]!.x).toBe(1);
  });
});

// ─── Map ─────────────────────────────────────────────────────────────────────

describe("JsonSerializer — map", () => {
  class WithMetadata {
    constructor(readonly metadata: Map<string, number>) {}
  }

  const spec: TypeSpec<WithMetadata> = {
    type: WithMetadata,
    factory: (p) => new WithMetadata(p.metadata as Map<string, number>),
    properties: [
      {
        ts: "metadata",
        json: "metadata",
        type: { kind: "map", key: { kind: "primitive" }, value: { kind: "primitive" } },
      },
    ],
  };

  test("serialize Map to plain object", () => {
    const obj = new WithMetadata(
      new Map([
        ["a", 1],
        ["b", 2],
      ]),
    );
    const json = JsonSerializer.serialize(obj, spec);
    expect(json.metadata).toEqual({ a: 1, b: 2 });
  });

  test("deserialize plain object to Map", () => {
    const obj = JsonSerializer.deserialize({ metadata: { x: 10, y: 20 } }, spec);
    expect(obj.metadata).toBeInstanceOf(Map);
    expect(obj.metadata.get("x")).toBe(10);
    expect(obj.metadata.get("y")).toBe(20);
  });
});

// ─── HashSet ─────────────────────────────────────────────────────────────────

describe("JsonSerializer — hashSet", () => {
  class WithSet {
    constructor(readonly items: HashSet<number>) {}
  }

  const spec: TypeSpec<WithSet> = {
    type: WithSet,
    factory: (p) => new WithSet(p.items as HashSet<number>),
    properties: [
      {
        ts: "items",
        json: "items",
        type: { kind: "hashSet", element: { kind: "primitive" } },
      },
    ],
  };

  test("serialize HashSet to array", () => {
    const set = new HashSet([1, 2, 3]);
    const obj = new WithSet(set);
    const json = JsonSerializer.serialize(obj, spec);
    const arr = json.items as number[];
    expect(arr.sort()).toEqual([1, 2, 3]);
  });

  test("deserialize array to HashSet", () => {
    const obj = JsonSerializer.deserialize({ items: [1, 2, 3] }, spec);
    expect(obj.items).toBeInstanceOf(HashSet);
    expect(obj.items.has(1)).toBe(true);
    expect(obj.items.has(4)).toBe(false);
  });
});

// ─── Branded (InlineWrapper) ─────────────────────────────────────────────────

describe("JsonSerializer — branded", () => {
  type UserId = string & { readonly __brand: "UserId" };

  const UserId = {
    create: (value: string): UserId => value as UserId,
  };

  class User {
    constructor(
      readonly id: UserId,
      readonly name: string,
    ) {}
  }

  const spec: TypeSpec<User> = {
    type: User,
    factory: (p) => new User(p.id as UserId, p.name as string),
    properties: [
      { ts: "id", json: "user_id", type: { kind: "branded", create: UserId.create } },
      { ts: "name", json: "name", type: { kind: "primitive" } },
    ],
  };

  test("serialize branded — brand erases, passthrough", () => {
    const user = new User(UserId.create("abc123"), "Alice");
    const json = JsonSerializer.serialize(user, spec);
    expect(json.user_id).toBe("abc123");
  });

  test("deserialize branded — calls create()", () => {
    const user = JsonSerializer.deserialize({ user_id: "abc123", name: "Alice" }, spec);
    expect(user.id).toBe("abc123");
    expect(user.name).toBe("Alice");
  });
});

// ─── String Enum ─────────────────────────────────────────────────────────────

describe("JsonSerializer — enum (string)", () => {
  const Status = { Draft: "Draft", Active: "Active", Completed: "Completed" } as const;

  class Task {
    constructor(
      readonly title: string,
      readonly status: string,
    ) {}
  }

  const spec: TypeSpec<Task> = {
    type: Task,
    factory: (p) => new Task(p.title as string, p.status as string),
    properties: [
      { ts: "title", json: "title", type: { kind: "primitive" } },
      { ts: "status", json: "status", type: { kind: "enum", values: Status } },
    ],
  };

  test("serialize enum — passthrough", () => {
    const task = new Task("Do it", "Active");
    const json = JsonSerializer.serialize(task, spec);
    expect(json.status).toBe("Active");
  });

  test("deserialize valid enum value", () => {
    const task = JsonSerializer.deserialize({ title: "Do it", status: "Draft" }, spec);
    expect(task.status).toBe("Draft");
  });

  test("deserialize invalid enum throws", () => {
    expect(() => {
      JsonSerializer.deserialize({ title: "X", status: "Invalid" }, spec);
    }).toThrow(/Invalid enum value/);
  });
});

// ─── Numeric Enum ────────────────────────────────────────────────────────────

describe("JsonSerializer — numericEnum", () => {
  const Priority = { Low: 0, Medium: 1, High: 2 } as const;

  class Item {
    constructor(readonly priority: number) {}
  }

  const spec: TypeSpec<Item> = {
    type: Item,
    factory: (p) => new Item(p.priority as number),
    properties: [
      { ts: "priority", json: "priority", type: { kind: "numericEnum", values: Priority } },
    ],
  };

  test("round-trip numeric enum", () => {
    const item = new Item(1);
    const json = JsonSerializer.serialize(item, spec);
    expect(json.priority).toBe(1);
    const restored = JsonSerializer.deserialize(json, spec);
    expect(restored.priority).toBe(1);
  });

  test("invalid numeric enum throws", () => {
    expect(() => {
      JsonSerializer.deserialize({ priority: 99 }, spec);
    }).toThrow(/Invalid numeric enum value/);
  });
});

// ─── Temporal ────────────────────────────────────────────────────────────────

describe("JsonSerializer — temporal", () => {
  // Simulate Temporal.PlainDate-like object (no real dependency needed)
  class FakeDate {
    constructor(readonly isoString: string) {}
    toString() {
      return this.isoString;
    }
    static from(iso: string) {
      return new FakeDate(iso);
    }
  }

  class Event {
    constructor(
      readonly name: string,
      readonly date: FakeDate,
    ) {}
  }

  const spec: TypeSpec<Event> = {
    type: Event,
    factory: (p) => new Event(p.name as string, p.date as FakeDate),
    properties: [
      { ts: "name", json: "name", type: { kind: "primitive" } },
      { ts: "date", json: "date", type: { kind: "temporal", parse: FakeDate.from } },
    ],
  };

  test("serialize temporal — calls toString()", () => {
    const event = new Event("Launch", new FakeDate("2024-01-15"));
    const json = JsonSerializer.serialize(event, spec);
    expect(json.date).toBe("2024-01-15");
  });

  test("deserialize temporal — calls parse()", () => {
    const event = JsonSerializer.deserialize({ name: "Launch", date: "2024-01-15" }, spec);
    expect(event.date).toBeInstanceOf(FakeDate);
    expect(event.date.isoString).toBe("2024-01-15");
  });
});

// ─── Decimal ─────────────────────────────────────────────────────────────────

describe("JsonSerializer — decimal", () => {
  // Simulate Decimal-like object (no hard dependency on decimal.js)
  class FakeDecimal {
    constructor(readonly value: number) {}
    toNumber() {
      return this.value;
    }
  }

  class Product {
    constructor(
      readonly name: string,
      readonly price: FakeDecimal,
    ) {}
  }

  const spec: TypeSpec<Product> = {
    type: Product,
    factory: (p) => new Product(p.name as string, new FakeDecimal(p.price as number)),
    properties: [
      { ts: "name", json: "name", type: { kind: "primitive" } },
      { ts: "price", json: "price", type: { kind: "decimal" } },
    ],
  };

  test("serialize decimal — calls toNumber()", () => {
    const product = new Product("Widget", new FakeDecimal(19.99));
    const json = JsonSerializer.serialize(product, spec);
    expect(json.price).toBe(19.99);
  });

  test("deserialize decimal — factory constructs it", () => {
    const product = JsonSerializer.deserialize({ name: "Widget", price: 19.99 }, spec);
    expect(product.price).toBeInstanceOf(FakeDecimal);
    expect(product.price.toNumber()).toBe(19.99);
  });
});

// ─── Inheritance ─────────────────────────────────────────────────────────────

describe("JsonSerializer — inheritance", () => {
  class Animal {
    constructor(
      readonly name: string,
      readonly legs: number,
    ) {}
  }

  class Dog extends Animal {
    constructor(
      name: string,
      legs: number,
      readonly breed: string,
    ) {
      super(name, legs);
    }
  }

  const animalSpec: TypeSpec<Animal> = {
    type: Animal,
    factory: (p) => new Animal(p.name as string, p.legs as number),
    properties: [
      { ts: "name", json: "name", type: { kind: "primitive" } },
      { ts: "legs", json: "legs", type: { kind: "primitive" } },
    ],
  };

  const dogSpec: TypeSpec<Dog> = {
    type: Dog,
    base: animalSpec,
    factory: (p) => new Dog(p.name as string, p.legs as number, p.breed as string),
    properties: [{ ts: "breed", json: "breed", type: { kind: "primitive" } }],
  };

  test("collectProperties walks base chain", () => {
    const props = collectProperties(dogSpec);
    expect(props.length).toBe(3);
    expect(props[0]!.ts).toBe("name");
    expect(props[1]!.ts).toBe("legs");
    expect(props[2]!.ts).toBe("breed");
  });

  test("serialize derived type includes base props", () => {
    const dog = new Dog("Rex", 4, "Labrador");
    const json = JsonSerializer.serialize(dog, dogSpec);
    expect(json).toEqual({ name: "Rex", legs: 4, breed: "Labrador" });
  });

  test("deserialize derived type", () => {
    const dog = JsonSerializer.deserialize({ name: "Rex", legs: 4, breed: "Labrador" }, dogSpec);
    expect(dog).toBeInstanceOf(Dog);
    expect(dog.name).toBe("Rex");
    expect(dog.breed).toBe("Labrador");
  });
});

// ─── Custom Converters ───────────────────────────────────────────────────────

describe("JsonSerializer — custom converters", () => {
  class FakeDecimal {
    constructor(readonly value: string) {}
    toNumber() {
      return parseFloat(this.value);
    }
    toString() {
      return this.value;
    }
  }

  class Price {
    constructor(readonly amount: FakeDecimal) {}
  }

  const spec: TypeSpec<Price> = {
    type: Price,
    factory: (p) => new Price(new FakeDecimal(p.amount as string)),
    properties: [{ ts: "amount", json: "amount", type: { kind: "decimal" } }],
  };

  const decimalAsString: JsonConverter = {
    kind: "decimal",
    serialize: (v) => (v as FakeDecimal).toString(),
    deserialize: (v) => v, // factory handles construction
  };

  test("custom converter overrides built-in", () => {
    const price = new Price(new FakeDecimal("123.456789012345"));
    const json = JsonSerializer.serialize(price, spec, [decimalAsString]);

    // With custom converter: serialized as string, not number
    expect(json.amount).toBe("123.456789012345");
    expect(typeof json.amount).toBe("string");
  });

  test("without custom converter, uses built-in (toNumber)", () => {
    const price = new Price(new FakeDecimal("19.99"));
    const json = JsonSerializer.serialize(price, spec);
    expect(json.amount).toBe(19.99);
    expect(typeof json.amount).toBe("number");
  });
});

// ─── SerializerContext ───────────────────────────────────────────────────────

describe("SerializerContext", () => {
  class TestContext extends SerializerContext {
    private _point?: TypeSpec<Point>;
    get point(): TypeSpec<Point> {
      return (this._point ??= this.createSpec(pointSpec));
    }

    private _person?: TypeSpec<Person>;
    get person(): TypeSpec<Person> {
      return (this._person ??= this.createSpec(personSpec));
    }
  }

  test("resolve returns registered spec", () => {
    const ctx = new TestContext();
    // Access lazy getter to trigger registration
    ctx.point;
    const resolved = ctx.resolve(Point);
    expect(resolved).toBeDefined();
    expect(resolved!.type).toBe(Point);
  });

  test("resolve returns undefined for unregistered type", () => {
    const ctx = new TestContext();
    expect(ctx.resolve(Point)).toBeUndefined();
  });

  test("withContext creates bound serializer", () => {
    const ctx = new TestContext();
    const s = JsonSerializer.withContext(ctx);

    // Trigger registration
    ctx.point;

    const spec = s.specFor(Point);
    expect(spec.type).toBe(Point);

    const point = new Point(10, 20);
    const json = s.serialize(point, spec);
    expect(json).toEqual({ x: 10, y: 20 });
  });

  test("bound serializer throws for unregistered type", () => {
    const ctx = new TestContext();
    const s = JsonSerializer.withContext(ctx);

    expect(() => s.specFor(Point)).toThrow(/No TypeSpec registered/);
  });
});

// ─── Error Cases ─────────────────────────────────────────────────────────────

describe("JsonSerializer — error cases", () => {
  test("deserialize null throws", () => {
    expect(() => {
      JsonSerializer.deserialize(null, personSpec);
    }).toThrow(/Expected object/);
  });

  test("deserialize non-object throws", () => {
    expect(() => {
      JsonSerializer.deserialize("not an object", personSpec);
    }).toThrow(/Expected object/);
  });

  test("missing required property throws", () => {
    expect(() => {
      JsonSerializer.deserialize({ first_name: "Alice" }, personSpec);
    }).toThrow(/Missing required property.*"age"/);
  });
});

// ─── Complex Combined Scenario ───────────────────────────────────────────────

describe("JsonSerializer — complex scenario", () => {
  type OrderId = string & { readonly __brand: "OrderId" };
  const OrderId = { create: (v: string): OrderId => v as OrderId };

  const Status = { Draft: "Draft", Active: "Active" } as const;

  class Order {
    constructor(
      readonly id: OrderId,
      readonly status: string,
      readonly items: Point[],
      readonly metadata: Map<string, number>,
      readonly note: string | null,
    ) {}
  }

  const orderSpec: TypeSpec<Order> = {
    type: Order,
    factory: (p) =>
      new Order(
        p.id as OrderId,
        p.status as string,
        p.items as Point[],
        p.metadata as Map<string, number>,
        p.note as string | null,
      ),
    properties: [
      { ts: "id", json: "order_id", type: { kind: "branded", create: OrderId.create } },
      { ts: "status", json: "current_status", type: { kind: "enum", values: Status } },
      {
        ts: "items",
        json: "items",
        type: { kind: "array", element: { kind: "ref", spec: () => pointSpec } },
      },
      {
        ts: "metadata",
        json: "meta",
        type: { kind: "map", key: { kind: "primitive" }, value: { kind: "primitive" } },
      },
      {
        ts: "note",
        json: "note",
        type: { kind: "nullable", inner: { kind: "primitive" } },
        optional: true,
      },
    ],
  };

  test("full round-trip with mixed types", () => {
    const order = new Order(
      OrderId.create("ord-001"),
      "Active",
      [new Point(1, 2), new Point(3, 4)],
      new Map([
        ["priority", 5],
        ["weight", 10],
      ]),
      null,
    );

    const json = JsonSerializer.serialize(order, orderSpec);

    expect(json).toEqual({
      order_id: "ord-001",
      current_status: "Active",
      items: [
        { x: 1, y: 2 },
        { x: 3, y: 4 },
      ],
      meta: { priority: 5, weight: 10 },
      note: null,
    });

    const restored = JsonSerializer.deserialize(json, orderSpec);

    expect(restored).toBeInstanceOf(Order);
    expect(restored.id).toBe("ord-001");
    expect(restored.status).toBe("Active");
    expect(restored.items[0]).toBeInstanceOf(Point);
    expect(restored.items[0]!.x).toBe(1);
    expect(restored.metadata).toBeInstanceOf(Map);
    expect(restored.metadata.get("priority")).toBe(5);
    expect(restored.note).toBeNull();
  });
});
