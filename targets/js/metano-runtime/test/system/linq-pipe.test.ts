import { describe, expect, test } from "bun:test";
import {
  aggregate,
  all,
  any,
  append,
  concat,
  contains,
  count,
  distinct,
  distinctBy,
  first,
  firstOrDefault,
  last,
  orderBy,
  orderByDescending,
  pipe,
  select,
  selectMany,
  skip,
  skipWhile,
  sum,
  take,
  takeWhile,
  toArray,
  toMap,
  toSet,
  where,
  zip,
} from "../../src/system/linq-pipe/index.ts";

describe("linq-pipe operators", () => {
  test("where + select + toArray", () => {
    const result = pipe(
      [1, 2, 3, 4, 5],
      where((x) => x % 2 === 0),
      select((x) => x * 10),
      toArray(),
    );
    expect(result).toEqual([20, 40]);
  });

  test("take short-circuits", () => {
    let visited = 0;
    function* infinite(): Iterable<number> {
      for (let i = 0; ; i++) {
        visited++;
        yield i;
      }
    }
    const result = pipe(infinite(), take<number>(3), toArray());
    expect(result).toEqual([0, 1, 2]);
    expect(visited).toBe(3);
  });

  test("skip", () => {
    expect(pipe([1, 2, 3, 4, 5], skip<number>(2), toArray())).toEqual([3, 4, 5]);
  });

  test("takeWhile + skipWhile", () => {
    expect(
      pipe(
        [1, 2, 3, 4, 1],
        takeWhile<number>((x) => x < 3),
        toArray(),
      ),
    ).toEqual([1, 2]);
    expect(
      pipe(
        [1, 2, 3, 4, 1],
        skipWhile<number>((x) => x < 3),
        toArray(),
      ),
    ).toEqual([3, 4, 1]);
  });

  test("distinct + distinctBy", () => {
    expect(pipe([1, 1, 2, 3, 2], distinct<number>(), toArray())).toEqual([1, 2, 3]);
    type Person = { id: number; name: string };
    const people: Person[] = [
      { id: 1, name: "a" },
      { id: 2, name: "b" },
      { id: 1, name: "c" },
    ];
    expect(
      pipe(
        people,
        distinctBy((p) => p.id),
        toArray(),
      ),
    ).toEqual([
      { id: 1, name: "a" },
      { id: 2, name: "b" },
    ]);
  });

  test("selectMany flattens", () => {
    expect(
      pipe(
        [1, 2, 3],
        selectMany((x) => [x, x * 10]),
        toArray(),
      ),
    ).toEqual([1, 10, 2, 20, 3, 30]);
  });

  test("orderBy / orderByDescending", () => {
    expect(
      pipe(
        [3, 1, 2],
        orderBy<number, number>((x) => x),
        toArray(),
      ),
    ).toEqual([1, 2, 3]);
    expect(
      pipe(
        [3, 1, 2],
        orderByDescending<number, number>((x) => x),
        toArray(),
      ),
    ).toEqual([3, 2, 1]);
  });

  test("concat + append + prepend", () => {
    expect(pipe([1, 2], concat([3, 4]), toArray())).toEqual([1, 2, 3, 4]);
    expect(pipe([1, 2], append<number>(99), toArray())).toEqual([1, 2, 99]);
  });

  test("zip", () => {
    expect(
      pipe(
        [1, 2, 3],
        zip([10, 20, 30], (a: number, b: number) => a + b),
        toArray(),
      ),
    ).toEqual([11, 22, 33]);
  });

  test("first / firstOrDefault", () => {
    expect(pipe([1, 2, 3], first<number>())).toBe(1);
    expect(pipe([1, 2, 3], firstOrDefault<number>((x) => x > 10))).toBeNull();
    expect(() => pipe([] as number[], first<number>())).toThrow();
  });

  test("last", () => {
    expect(pipe([1, 2, 3], last<number>())).toBe(3);
  });

  test("any / all / count / contains", () => {
    expect(pipe([1, 2, 3], any<number>((x) => x > 2))).toBe(true);
    expect(pipe([1, 2, 3], any<number>((x) => x > 10))).toBe(false);
    expect(pipe([1, 2, 3], all<number>((x) => x > 0))).toBe(true);
    expect(pipe([1, 2, 3], all<number>((x) => x > 1))).toBe(false);
    expect(pipe([1, 2, 3], count<number>())).toBe(3);
    expect(pipe([1, 2, 3], contains<number>(2))).toBe(true);
  });

  test("sum + aggregate", () => {
    expect(pipe([1, 2, 3], sum<number>())).toBe(6);
    expect(pipe([1, 2, 3], aggregate(0, (a: number, b: number) => a + b * 2))).toBe(12);
  });

  test("toMap / toSet", () => {
    type Item = { id: number; name: string };
    const items: Item[] = [
      { id: 1, name: "a" },
      { id: 2, name: "b" },
    ];
    const map = pipe(
      items,
      toMap(
        (i: Item) => i.id,
        (i: Item) => i.name,
      ),
    );
    expect(map.get(1)).toBe("a");
    expect(map.get(2)).toBe("b");

    const set = pipe([1, 2, 2, 3], toSet<number>());
    expect(set.size).toBe(3);
  });

  test("lazy semantics — generators not executed until terminal", () => {
    let calls = 0;
    function* counter(): Iterable<number> {
      for (let i = 0; i < 1000; i++) {
        calls++;
        yield i;
      }
    }
    pipe(
      counter(),
      where<number>((x) => x % 2 === 0),
      select((x) => x * 2),
      take<number>(2),
      toArray(),
    );
    // take(2) over even numbers stops at i=2 → 3 yields visited.
    expect(calls).toBe(3);
  });
});
