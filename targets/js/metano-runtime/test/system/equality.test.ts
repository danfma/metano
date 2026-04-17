import { describe, expect, test } from "bun:test";
import { equals, hashCode } from "#/system/equality.ts";

describe("equals", () => {
  describe("primitives", () => {
    test("same primitives are equal", () => {
      expect(equals(1, 1)).toBe(true);
      expect(equals("a", "a")).toBe(true);
      expect(equals(true, true)).toBe(true);
      expect(equals(null, null)).toBe(true);
      expect(equals(undefined, undefined)).toBe(true);
    });

    test("different primitives are not equal", () => {
      expect(equals(1, 2)).toBe(false);
      expect(equals("a", "b")).toBe(false);
      expect(equals(true, false)).toBe(false);
    });

    test("null and undefined are not equal", () => {
      expect(equals(null, undefined)).toBe(false);
      expect(equals(undefined, null)).toBe(false);
    });

    test("NaN equals NaN (mirrors C# Double.NaN.Equals)", () => {
      expect(equals(NaN, NaN)).toBe(true);
    });

    test("different types are not equal", () => {
      expect(equals(1, "1")).toBe(false);
      expect(equals(0, false)).toBe(false);
    });
  });

  describe("arrays", () => {
    test("empty arrays are equal", () => {
      expect(equals([], [])).toBe(true);
    });

    test("element-wise comparison", () => {
      expect(equals([1, 2, 3], [1, 2, 3])).toBe(true);
      expect(equals([1, 2, 3], [1, 2, 4])).toBe(false);
    });

    test("different lengths are not equal", () => {
      expect(equals([1, 2], [1, 2, 3])).toBe(false);
    });

    test("order matters", () => {
      expect(equals([1, 2, 3], [3, 2, 1])).toBe(false);
    });

    test("nested arrays compare deeply", () => {
      expect(equals([[1, 2], [3]], [[1, 2], [3]])).toBe(true);
      expect(equals([[1, 2], [3]], [[1, 2], [4]])).toBe(false);
    });

    test("array vs non-array is not equal", () => {
      expect(equals([1, 2], { 0: 1, 1: 2, length: 2 })).toBe(false);
    });
  });

  describe("Maps", () => {
    test("empty Maps are equal", () => {
      expect(equals(new Map(), new Map())).toBe(true);
    });

    test("same content, same insertion order", () => {
      const a = new Map([
        ["x", 1],
        ["y", 2],
      ]);
      const b = new Map([
        ["x", 1],
        ["y", 2],
      ]);
      expect(equals(a, b)).toBe(true);
    });

    test("same content, different insertion order", () => {
      const a = new Map([
        ["x", 1],
        ["y", 2],
      ]);
      const b = new Map([
        ["y", 2],
        ["x", 1],
      ]);
      expect(equals(a, b)).toBe(true);
    });

    test("different values are not equal", () => {
      const a = new Map([["x", 1]]);
      const b = new Map([["x", 2]]);
      expect(equals(a, b)).toBe(false);
    });

    test("different keys are not equal", () => {
      const a = new Map([["x", 1]]);
      const b = new Map([["y", 1]]);
      expect(equals(a, b)).toBe(false);
    });
  });

  describe("Sets", () => {
    test("empty Sets are equal", () => {
      expect(equals(new Set(), new Set())).toBe(true);
    });

    test("same membership, any order", () => {
      expect(equals(new Set([1, 2, 3]), new Set([3, 2, 1]))).toBe(true);
    });

    test("different membership is not equal", () => {
      expect(equals(new Set([1, 2]), new Set([1, 3]))).toBe(false);
    });
  });

  describe("Dates", () => {
    test("same epoch is equal", () => {
      expect(equals(new Date(1000), new Date(1000))).toBe(true);
    });

    test("different epochs are not equal", () => {
      expect(equals(new Date(1000), new Date(2000))).toBe(false);
    });
  });

  describe("plain objects", () => {
    test("empty objects are equal", () => {
      expect(equals({}, {})).toBe(true);
    });

    test("same keys + values", () => {
      expect(equals({ a: 1, b: 2 }, { a: 1, b: 2 })).toBe(true);
    });

    test("key order doesn't matter", () => {
      expect(equals({ a: 1, b: 2 }, { b: 2, a: 1 })).toBe(true);
    });

    test("different value at same key", () => {
      expect(equals({ a: 1 }, { a: 2 })).toBe(false);
    });

    test("missing key", () => {
      expect(equals({ a: 1, b: 2 }, { a: 1 })).toBe(false);
    });

    test("extra key", () => {
      expect(equals({ a: 1 }, { a: 1, b: 2 })).toBe(false);
    });

    test("nested compare", () => {
      const a = { user: { name: "Ana", age: 30 }, tags: ["dev", "music"] };
      const b = { user: { name: "Ana", age: 30 }, tags: ["dev", "music"] };
      expect(equals(a, b)).toBe(true);
    });
  });

  describe("custom equals method", () => {
    test("delegates to instance equals", () => {
      class Money {
        constructor(
          public amount: number,
          public currency: string,
        ) {}
        equals(other: unknown): boolean {
          return (
            other instanceof Money &&
            other.amount === this.amount &&
            other.currency === this.currency
          );
        }
      }
      expect(equals(new Money(10, "USD"), new Money(10, "USD"))).toBe(true);
      expect(equals(new Money(10, "USD"), new Money(10, "EUR"))).toBe(false);
    });
  });
});

describe("hashCode", () => {
  test("null and undefined hash to 0", () => {
    expect(hashCode(null)).toBe(0);
    expect(hashCode(undefined)).toBe(0);
  });

  test("equal primitives hash equal", () => {
    expect(hashCode(42)).toBe(hashCode(42));
    expect(hashCode("hello")).toBe(hashCode("hello"));
  });

  test("equal arrays hash equal", () => {
    expect(hashCode([1, 2, 3])).toBe(hashCode([1, 2, 3]));
  });

  test("array order matters", () => {
    expect(hashCode([1, 2, 3])).not.toBe(hashCode([3, 2, 1]));
  });

  test("equal plain objects hash equal regardless of key order", () => {
    expect(hashCode({ a: 1, b: 2 })).toBe(hashCode({ b: 2, a: 1 }));
  });

  test("different objects hash differently", () => {
    expect(hashCode({ a: 1 })).not.toBe(hashCode({ a: 2 }));
  });

  test("equal Maps hash equal regardless of insertion order", () => {
    const a = new Map([
      ["x", 1],
      ["y", 2],
    ]);
    const b = new Map([
      ["y", 2],
      ["x", 1],
    ]);
    expect(hashCode(a)).toBe(hashCode(b));
  });

  test("equal Sets hash equal regardless of insertion order", () => {
    expect(hashCode(new Set([1, 2, 3]))).toBe(hashCode(new Set([3, 2, 1])));
  });

  test("delegates to custom hashCode method", () => {
    class Tagged {
      constructor(public tag: string) {}
      hashCode(): number {
        return 12345;
      }
    }
    expect(hashCode(new Tagged("a"))).toBe(12345);
    expect(hashCode(new Tagged("b"))).toBe(12345);
  });

  test("equals/hashCode contract: equal values must hash equal", () => {
    const cases: Array<[unknown, unknown]> = [
      [
        { a: 1, b: [2, 3] },
        { b: [2, 3], a: 1 },
      ],
      [new Map([["k", "v"]]), new Map([["k", "v"]])],
      [new Set(["a", "b"]), new Set(["b", "a"])],
      [
        [1, "two", true],
        [1, "two", true],
      ],
    ];
    for (const [a, b] of cases) {
      expect(equals(a, b)).toBe(true);
      expect(hashCode(a)).toBe(hashCode(b));
    }
  });
});
