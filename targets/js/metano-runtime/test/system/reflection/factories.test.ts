import { describe, expect, test } from "bun:test";
import { implementsInterface } from "#/system/reflection/implements-interface.ts";
import { nullableType } from "#/system/reflection/nullable-type.ts";
import { DoubleType, Int32Type } from "#/system/reflection/primitive-types.ts";
import { specialize } from "#/system/reflection/specialize.ts";
import { withImplementers } from "#/system/reflection/with-implementers.ts";
import { Circle, FlatDto$rt, IFlat$rt, IShape$rt, type IFlat } from "./fixtures.ts";
import type { PlainObjectType } from "#/system/reflection/index.ts";

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
    expect(withImplementers(IShape$rt, []).test(new Circle())).toBe(IShape$rt.test(new Circle()));
    expect(implementsInterface(new Circle(), IShape$rt)).toBe(true);
  });
});
