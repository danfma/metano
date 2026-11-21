import { describe, expect, test } from "bun:test";
import { MissingMethodException } from "#/system/exceptions/missing-method-exception.ts";
import { createInstance } from "#/system/reflection/create-instance.ts";
import { implementsInterface } from "#/system/reflection/implements-interface.ts";
import { nullableType } from "#/system/reflection/nullable-type.ts";
import { opaqueType } from "#/system/reflection/opaque-type.ts";
import { DoubleType, Int32Type, StringType } from "#/system/reflection/primitive-types.ts";
import { specialize } from "#/system/reflection/specialize.ts";
import { tupleType } from "#/system/reflection/tuple-type.ts";
import { UnknownType } from "#/system/reflection/well-known-types.ts";
import { withImplementers } from "#/system/reflection/with-implementers.ts";
import { Circle, FlatDto$rt, IFlat$rt, IShape$rt, type IFlat } from "./fixtures.ts";
import type { ClassType, PlainObjectType, RuntimeType } from "#/system/reflection/index.ts";

describe("nullableType", () => {
  test("is memoized on the inner descriptor", () => {
    expect(nullableType(Int32Type)).toBe(nullableType(Int32Type));
    expect(nullableType(Int32Type)).not.toBe(nullableType(DoubleType));
  });

  test("accepts null or the inner type", () => {
    const type = nullableType(Int32Type);

    expect(type).toMatchObject({ kind: "nullable", name: "Nullable<Int32>" });
    expect(type.inner()).toBe(Int32Type);
    expect(type.test(null)).toBe(true);
    expect(type.test(1)).toBe(true);
    expect(type.test("a")).toBe(false);
    expect(type.test(undefined)).toBe(false);
  });
});

describe("specialize", () => {
  const open = Circle.$runtimeType;

  test("is memoized on the open type and the argument identities", () => {
    expect(specialize(open, [Int32Type])).toBe(specialize(open, [Int32Type]));
    expect(specialize(open, [Int32Type])).not.toBe(specialize(open, [DoubleType]));
    expect(specialize(open, [Int32Type, DoubleType])).not.toBe(specialize(open, [Int32Type]));
    expect(specialize(open, [])).not.toBe(open);
  });

  test("names the specialization and keeps the open type's test", () => {
    const closed = specialize(open, [Int32Type]);

    expect(closed.name).toBe("Circle<Int32>");
    expect(closed.fullName).toBe("Geo.Circle<System.Int32>");
    expect(closed.typeArguments).toEqual([Int32Type]);
    expect(closed.test(new Circle())).toBe(true);
  });
});

describe("withImplementers", () => {
  const extra: PlainObjectType<IFlat> = {
    ...FlatDto$rt,
    name: "Other",
    test: (v: unknown): v is IFlat => typeof v === "object" && v !== null && "other" in v,
  };

  test("is memoized", () => {
    expect(withImplementers(IFlat$rt, [extra])).toBe(withImplementers(IFlat$rt, [extra]));
    expect(withImplementers(IFlat$rt, [extra])).not.toBe(withImplementers(IFlat$rt, []));
  });

  test("appends the implementers", () => {
    const augmented = withImplementers(IFlat$rt, [extra]);

    expect(augmented.plainImplementers.map((plain) => plain())).toEqual([FlatDto$rt, extra]);
  });

  test("tests plain objects against the added implementers", () => {
    const augmented = withImplementers(IShape$rt, [extra as never]);

    expect(augmented.test({ other: 1 })).toBe(true);
    expect(augmented.test({ nothing: 1 })).toBe(false);
  });

  test("keeps matching class instances through the original interface", () => {
    expect(IShape$rt.test(new Circle())).toBe(true);
    expect(withImplementers(IShape$rt, []).test(new Circle())).toBe(true);
    expect(implementsInterface(new Circle(), IShape$rt)).toBe(true);
  });
});

describe("specialize slot matching and create forwarding (W5-5 a, f)", () => {
  class Box<T> {
    declare readonly $typeArguments$Box: readonly RuntimeType[];

    constructor(typeArgs?: readonly RuntimeType[]) {
      Object.defineProperty(this, "$typeArguments$Box", {
        value: typeArgs ?? [UnknownType],
        enumerable: false,
      });
    }

    static get $runtimeType(): ClassType<Box<unknown>> {
      return Box$rt;
    }
  }

  const Box$rt: ClassType<Box<unknown>> = {
    kind: "class",
    name: "Box",
    fullName: "App.Box",
    namespace: "App",
    ctor: Box,
    interfaces: [],
    typeParameters: ["T"],
    slot: "$typeArguments$Box",
    isAbstract: false,
    isValueType: false,
    create: (typeArgs) => new Box(typeArgs),
    test: (v: unknown): v is Box<unknown> => v instanceof Box,
  };

  test("checks open.slot when present and treats UnknownType as wildcard", () => {
    const intBox = specialize(Box$rt, [Int32Type]);
    const strBox = specialize(Box$rt, [StringType]);
    const unknownBox = specialize(Box$rt, [UnknownType]);

    expect(intBox.test(new Box([Int32Type]))).toBe(true);
    expect(strBox.test(new Box([Int32Type]))).toBe(false);
    expect(unknownBox.test(new Box([Int32Type]))).toBe(true);
    expect(intBox.test(new Box())).toBe(true);
  });

  test("forwards type arguments through create", () => {
    const intBox = specialize(Box$rt, [Int32Type]);
    const created = createInstance(intBox);

    expect(created).toBeInstanceOf(Box);
    expect(created.$typeArguments$Box).toEqual([Int32Type]);
  });
});

describe("createInstance", () => {
  test("invokes create when present and throws MissingMethodException when absent", () => {
    expect(createInstance(Int32Type)).toBe(0);
    expect(() => createInstance(StringType)).toThrow(MissingMethodException);
    expect(() => createInstance(UnknownType)).toThrow(MissingMethodException);
  });
});

describe("opaqueType", () => {
  test("is memoized on fullName, equal only to itself, and never matches values", () => {
    const a1 = opaqueType("Lib.Hidden");
    const a2 = opaqueType("Lib.Hidden");
    const b = opaqueType("Lib.Other");

    expect(a1).toBe(a2);
    expect(a1).not.toBe(b);
    expect(a1.kind).toBe("opaque");
    expect(a1.name).toBe("Hidden");
    expect(a1.namespace).toBe("Lib");
    expect(a1.test({})).toBe(false);
    expect(a1.test(null)).toBe(false);
  });
});

describe("tupleType", () => {
  test("is memoized, tests elements, and provides default/create", () => {
    const t1 = tupleType([Int32Type, StringType]);
    const t2 = tupleType([Int32Type, StringType]);
    const t3 = tupleType([DoubleType, StringType]);

    expect(t1).toBe(t2);
    expect(t1).not.toBe(t3);
    expect(t1.test([1, "a"])).toBe(true);
    expect(t1.test([1.5, "a"])).toBe(false);
    expect(t1.test([1])).toBe(false);
    expect(t1.default?.()).toEqual([0, null]);
    expect(createInstance(t1)).toEqual([0, null]);
  });
});
