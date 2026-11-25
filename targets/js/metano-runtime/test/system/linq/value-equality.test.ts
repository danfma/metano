import { describe, expect, test } from "bun:test";
import type { IEqualityComparer } from "#/system/collections/default-comparer.ts";
import { Decimal } from "#/system/decimal.ts";
import { ArgumentNullException } from "#/system/exceptions/index.ts";
import {
  contains,
  groupBy,
  join,
  linq,
  Lookup,
  sequenceEqual,
  toArray,
  toLookup,
} from "#/system/linq/index.ts";

/** A record: value equality through `equals` and `hashCode`. */
class Item {
  readonly name: string;
  readonly qty: number;

  constructor(name: string, qty: number) {
    this.name = name;
    this.qty = qty;
  }

  equals(other: unknown): boolean {
    return other instanceof Item && other.name === this.name && other.qty === this.qty;
  }

  hashCode(): number {
    return this.qty;
  }
}

/** A class with reference equality, as a C# class without `Equals`. */
class Plain {
  readonly v: number;

  constructor(v: number) {
    this.v = v;
  }
}

const byName: IEqualityComparer<string> = {
  equals: (a, b) => a.toLowerCase() === b.toLowerCase(),
  getHashCode: (value) => value.toLowerCase().length,
};

/** Every expected value was printed by the same LINQ call on .NET 10.0. */
describe("contains", () => {
  test("finds a record, NaN and a decimal by value", () => {
    expect(linq([new Item("a", 1)], contains(new Item("a", 1)))).toBe(true);
    expect(linq([1, Number.NaN], contains(Number.NaN))).toBe(true);
    expect(linq([new Decimal("1.5")], contains(new Decimal("1.50")))).toBe(true);
  });

  test("finds null where a missing field reads undefined", () => {
    const notes = [{ name: "a" }].map((dto) => (dto as { note?: string | null }).note);

    expect(linq(notes, contains(null))).toBe(true);
    expect(linq(notes, contains("x"))).toBe(false);
    expect(linq([null], contains(null))).toBe(true);
  });

  test("keeps reference equality for a class without value equality", () => {
    const first = new Plain(1);

    expect(linq([first], contains(new Plain(1)))).toBe(false);
    expect(linq([first], contains(first))).toBe(true);
  });

  test("a comparer decides, called as equals(element, value)", () => {
    expect(linq(["Ab"], contains("aB", byName))).toBe(true);
    expect(linq(["Ab"], contains("aB", null))).toBe(false);
  });
});

describe("groupBy", () => {
  test("groups equal record keys together, in first-seen order", () => {
    const items = [new Item("b", 2), new Item("a", 1), new Item("b", 2)];
    const groups = [...linq(items, groupBy((item: Item) => item))];

    expect(groups.map((group) => group.key.name)).toEqual(["b", "a"]);
    expect(groups.map((group) => [...group].length)).toEqual([2, 1]);
  });

  test("keeps the first key instance and the source order of the elements", () => {
    const first = new Item("a", 1);
    const [group] = [...linq([first, new Item("a", 1)], groupBy((item: Item) => item))];

    expect(group!.key).toBe(first);
    expect([...group!][0]).toBe(first);
  });

  test("a reference-equality class keeps one group per instance", () => {
    expect([...linq([new Plain(1), new Plain(1)], groupBy((p: Plain) => p))].length).toBe(2);
  });

  test("groups NaN keys together", () => {
    expect([...linq([Number.NaN, Number.NaN], groupBy((x: number) => x))].length).toBe(1);
  });

  test("a comparer decides", () => {
    const groups = [...linq(["a", "A", "b"], groupBy((s: string) => s, byName))];

    expect(groups.map((group) => [...group])).toEqual([["a", "A"], ["b"]]);
  });
});

describe("deferred execution and null keys", () => {
  test("groupBy reads its source on the first next(), not when the iterator is made", () => {
    let reads = 0;
    const source = {
      *[Symbol.iterator]() {
        reads++;
        yield 1;
      },
    };
    const iterator = linq(source, groupBy((x: number) => x))[Symbol.iterator]();

    expect(reads).toBe(0);
    iterator.next();
    expect(reads).toBe(1);
  });

  test("a comparer never sees a null key in getHashCode, as in .NET's Lookup", () => {
    const length: IEqualityComparer<string | null> = {
      equals: (a, b) => a === b,
      getHashCode: (value) => value!.length,
    };
    const groups = [...linq(["a", null, "b", null], groupBy((s: string | null) => s, length))];

    expect(groups.map((group) => [group.key, [...group].length])).toEqual([
      ["a", 1],
      [null, 2],
      ["b", 1],
    ]);

    const lookup = linq(["a", null], toLookup((s: string | null) => s, length));

    expect(lookup.contains(null)).toBe(true);
  });

  test("join with an empty inner reads one outer element and never calls the key selector", () => {
    let reads = 0;
    let keyCalls = 0;
    const outer = {
      *[Symbol.iterator]() {
        while (true) {
          reads++;
          yield reads;
        }
      },
    };
    const joined = linq(
      outer,
      join(
        [] as number[],
        (o: number) => {
          keyCalls++;
          return o;
        },
        (i: number) => i,
        (o: number) => o,
      ),
    );

    expect([...joined]).toEqual([]);
    expect([reads, keyCalls]).toEqual([1, 0]);
  });
});

describe("sequenceEqual", () => {
  test("compares in order by value", () => {
    expect(linq([new Item("a", 1)], sequenceEqual([new Item("a", 1)]))).toBe(true);
    expect(linq([new Plain(1)], sequenceEqual([new Plain(1)]))).toBe(false);
    expect(linq([1, 2], sequenceEqual([2, 1]))).toBe(false);
    expect(linq([1, 2], sequenceEqual([1]))).toBe(false);
    expect(linq([Number.NaN], sequenceEqual([Number.NaN]))).toBe(true);
    expect(linq([] as number[], sequenceEqual([] as number[]))).toBe(true);
  });

  test("uses the comparer", () => {
    expect(linq(["a"], sequenceEqual(["A"], byName))).toBe(true);
  });

  test("a null second throws ArgumentNullException", () => {
    expect(() => linq([1], sequenceEqual(null as unknown as number[]))).toThrow(
      ArgumentNullException,
    );
  });
});

describe("toLookup", () => {
  test("groups by value, finds by value, and an absent key is empty", () => {
    const lookup = linq(
      [new Item("a", 1), new Item("b", 2), new Item("a", 1)],
      toLookup((item: Item) => item),
    );

    expect(lookup).toBeInstanceOf(Lookup);
    expect(lookup.count).toBe(2);
    expect([...lookup.get(new Item("a", 1))].length).toBe(2);
    expect(lookup.contains(new Item("b", 2))).toBe(true);
    expect([...lookup.get(new Item("z", 9))]).toEqual([]);
    expect([...lookup].map((group) => group.key.name)).toEqual(["a", "b"]);
  });

  test("takes an element selector, a comparer, or both", () => {
    const words = ["a", "A", "b"];

    expect(
      [...linq(words, toLookup((s: string) => s, (s: string) => s.length)).get("a")],
    ).toEqual([1]);
    expect(linq(words, toLookup((s: string) => s, byName)).count).toBe(2);
    expect(
      [...linq(words, toLookup((s: string) => s, (s: string) => s + "!", byName)).get("a")],
    ).toEqual(["a!", "A!"]);
  });
});

describe("join", () => {
  test("matches by value, outer order then inner order", () => {
    const outer = [new Item("a", 1), new Item("b", 2)];
    const inner = [new Item("b", 2), new Item("a", 1), new Item("a", 1)];
    const result = linq(
      outer,
      join(
        inner,
        (o: Item) => o,
        (i: Item) => i,
        (o: Item, i: Item) => `${o.name}${inner.indexOf(i)}`,
      ),
      toArray(),
    );

    expect(result).toEqual(["a1", "a2", "b0"]);
  });

  test("a null key matches nothing; a comparer decides", () => {
    const nulls = linq(
      [null, 1],
      join(
        [null, 1],
        (o: number | null) => o,
        (i: number | null) => i,
        (o, i) => [o, i],
      ),
      toArray(),
    );

    expect(nulls).toEqual([[1, 1]]);
    expect(
      linq(["a"], join(["A"], (o: string) => o, (i: string) => i, (o, i) => o + i, byName), toArray()),
    ).toEqual(["aA"]);
  });

  test("reads the inner sequence only when the outer has an element", () => {
    let reads = 0;
    const inner = {
      *[Symbol.iterator]() {
        reads++;
        yield 1;
      },
    };

    linq([] as number[], join(inner, (o: number) => o, (i: number) => i, (o) => o), toArray());
    expect(reads).toBe(0);
  });
});
