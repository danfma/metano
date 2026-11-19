import { describe, expect, test } from "bun:test";
import { defaultEquals, equals, hashCode, objectEquals } from "#/system/equality.ts";
import { InvalidCastException } from "#/system/exceptions/invalid-cast-exception.ts";
import { box, Boxed, boxNullable } from "#/system/reflection/box.ts";
import { castTo } from "#/system/reflection/cast-to.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { isBoxOf } from "#/system/reflection/is-box-of.ts";
import { asType, isType } from "#/system/reflection/is-type.ts";
import { nullableType } from "#/system/reflection/nullable-type.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";
import type { EnumType, PrimitiveType } from "#/system/reflection/runtime-type.ts";
import { unbox } from "#/system/reflection/unbox.ts";
import { ObjectType } from "#/system/reflection/well-known-types.ts";

function enumType(
  name: string,
  values: number[],
  names: string[],
  isFlags: boolean,
): EnumType<number> {
  return {
    kind: "enum",
    name,
    fullName: `Geo.${name}`,
    namespace: "Geo",
    values,
    names,
    isFlags,
    test: (v: unknown): v is number => typeof v === "number" && Number.isInteger(v),
  };
}

const Color = enumType("Color", [0, 1], ["Red", "Green"], false);
const Perms = enumType("Perms", [1, 2, 4], ["Read", "Write", "Execute"], true);
const PermsWithNone = enumType("Perms", [0, 1, 2], ["None", "Read", "Write"], true);
const Wide = enumType("Wide", [1, 2, 3, 4], ["A", "B", "AB", "C"], true);
const UserId: PrimitiveType<string> = {
  kind: "primitive",
  name: "UserId",
  fullName: "Geo.UserId",
  namespace: "Geo",
  primitive: "String",
  test: (v: unknown): v is string => typeof v === "string",
};

describe("Boxed.toString", () => {
  test("a declared value prints its member name", () => {
    expect(String(box(1, Color))).toBe("Green");
  });

  test("an undeclared value prints its number", () => {
    expect(String(box(99, Color))).toBe("99");
  });

  test("a flags value prints the names of its set bits", () => {
    expect(String(box(3, Perms))).toBe("Read, Write");
    expect(String(box(7, Perms))).toBe("Read, Write, Execute");
  });

  test("a flags value with a bit no member names prints its number, as .NET does", () => {
    expect(String(box(9, Perms))).toBe("9");
  });

  test("a declared combination prints its own name, then the greedy parts", () => {
    expect(String(box(3, Wide))).toBe("AB");
    expect(String(box(7, Wide))).toBe("AB, C");
  });

  test("members declared out of value order decompose as .NET does", () => {
    const Unsorted = enumType("Unsorted", [4, 1, 3, 2], ["C", "A", "AB", "B"], true);

    expect(String(box(7, Unsorted))).toBe("AB, C");
    expect(String(box(6, Unsorted))).toBe("B, C");
    expect(String(box(3, Unsorted))).toBe("AB");
  });

  test("zero prints the zero member, or 0 when there is none", () => {
    expect(String(box(0, PermsWithNone))).toBe("None");
    expect(String(box(0, Perms))).toBe("0");
  });

  test("a non-enum descriptor prints String(value)", () => {
    expect(String(box("u-1", UserId))).toBe("u-1");
  });

  test("interpolation renders the box through toString", () => {
    expect(`${box(1, Color)}`).toBe("Green");
  });
});

describe("Boxed value access", () => {
  test("valueOf and toJSON answer the raw value", () => {
    const boxed = box(1, Color);

    expect(boxed.valueOf()).toBe(1);
    expect(boxed.toJSON()).toBe(1);
    expect(JSON.stringify({ color: boxed })).toBe('{"color":1}');
  });

  test("unbox answers the raw value of a box and anything else unchanged", () => {
    const raw = { a: 1 };

    expect(unbox(box(1, Color))).toBe(1);
    expect(unbox(raw)).toBe(raw);
    expect(unbox(5)).toBe(5);
    expect(unbox(null)).toBeNull();
    expect(unbox(undefined)).toBeUndefined();
  });

  test("unbox is shallow", () => {
    const boxed = box(1, Color);

    expect(unbox([boxed])).toEqual([boxed]);
  });

  test("boxNullable keeps null and boxes the rest, evaluating the operand once", () => {
    let reads = 0;
    const read = (): number | null => {
      reads++;

      return 1;
    };

    expect(boxNullable(null, Color)).toBeNull();
    expect(boxNullable(read(), Color)).toBeInstanceOf(Boxed);
    expect(reads).toBe(1);
  });
});

describe("type tests over a box", () => {
  test("isType passes for a box of the type and fails for a box of another type", () => {
    expect(isType(box(1, Color), Color)).toBe(true);
    expect(isType(box(1, Color), Perms)).toBe(false);
    expect(isType(box(1, Color), Int32Type)).toBe(false);
  });

  test("a box of a type with the same fullName but another descriptor object passes", () => {
    expect(isType(box(1, Color), { ...Color })).toBe(true);
  });

  test("a box is an object", () => {
    expect(isType(box(1, Color), ObjectType)).toBe(true);
  });

  test("a nullable descriptor tests the box against its inner type", () => {
    expect(isType(box(1, Color), nullableType(Color))).toBe(true);
    expect(isType(box(1, Color), nullableType(Perms))).toBe(false);
  });

  test("a raw number keeps the descriptor's own test", () => {
    expect(isType(1, Color)).toBe(true);
    expect(isType(1.5, Color)).toBe(false);
  });

  test("asType answers the raw value for the boxed type, the box for object, null otherwise", () => {
    const boxed = box(1, Color);

    expect(asType(boxed, Color)).toBe(1);
    expect(asType(boxed, ObjectType)).toBe(boxed);
    expect(asType(boxed, Perms)).toBeNull();
    expect(asType(1, Color)).toBe(1);
    expect(asType(null, Color)).toBeNull();
  });

  test("castTo unwraps for the boxed type and throws for another", () => {
    const boxed = box(1, Color);

    expect(castTo(boxed, Color)).toBe(1);
    expect(castTo(1, Color)).toBe(1);
    expect(castTo(null, Color)).toBeNull();
    expect(() => castTo(boxed, Perms)).toThrow(InvalidCastException);
    expect(() => castTo(boxed, Perms)).toThrow(
      "Unable to cast object of type 'Geo.Color' to type 'Geo.Perms'.",
    );
  });

  test("isBoxOf is true only for a box of that type", () => {
    expect(isBoxOf(box(1, Color), Color)).toBe(true);
    expect(isBoxOf(box(1, Color), Perms)).toBe(false);
    expect(isBoxOf(1, Color)).toBe(false);
    expect(isBoxOf(null, Color)).toBe(false);
  });

  test("getRuntimeType answers the box's descriptor", () => {
    expect(getRuntimeType(box(1, Color))).toBe(Color);
  });
});

describe("equality and hashing", () => {
  test("two boxes of the same type and value are equal, descriptors by fullName", () => {
    expect(defaultEquals(box(1, Color), box(1, { ...Color }))).toBe(true);
  });

  test("boxes of another value or another type are not equal", () => {
    expect(defaultEquals(box(1, Color), box(0, Color))).toBe(false);
    expect(defaultEquals(box(1, Color), box(1, Perms))).toBe(false);
  });

  test("a box never equals a raw value, in either order", () => {
    expect(defaultEquals(box(1, Color), 1)).toBe(false);
    expect(defaultEquals(1, box(1, Color))).toBe(false);
  });

  test("objectEquals and equals follow the same strict rule", () => {
    for (const same of [objectEquals, equals]) {
      expect(same(box(1, Color), box(1, { ...Color }))).toBe(true);
      expect(same(box(1, Color), box(0, Color))).toBe(false);
      expect(same(box(1, Color), box(1, Perms))).toBe(false);
      expect(same(box(1, Color), 1)).toBe(false);
      expect(same(1, box(1, Color))).toBe(false);
    }
  });

  test("equal boxes hash alike and a different type hashes apart", () => {
    expect(hashCode(box(1, Color))).toBe(hashCode(box(1, { ...Color })));
    expect(hashCode(box(1, Color))).not.toBe(hashCode(box(1, Perms)));
    expect(hashCode(box(1, Color))).not.toBe(hashCode(box(0, Color)));
  });
});
