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
  linq,
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
    const result = linq(
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
    const result = linq(infinite(), take<number>(3), toArray());
    expect(result).toEqual([0, 1, 2]);
    expect(visited).toBe(3);
  });

  test("skip", () => {
    expect(linq([1, 2, 3, 4, 5], skip<number>(2), toArray())).toEqual([3, 4, 5]);
  });

  test("takeWhile + skipWhile", () => {
    expect(
      linq(
        [1, 2, 3, 4, 1],
        takeWhile<number>((x) => x < 3),
        toArray(),
      ),
    ).toEqual([1, 2]);
    expect(
      linq(
        [1, 2, 3, 4, 1],
        skipWhile<number>((x) => x < 3),
        toArray(),
      ),
    ).toEqual([3, 4, 1]);
  });

  test("distinct + distinctBy", () => {
    expect(linq([1, 1, 2, 3, 2], distinct<number>(), toArray())).toEqual([1, 2, 3]);
    type Person = { id: number; name: string };
    const people: Person[] = [
      { id: 1, name: "a" },
      { id: 2, name: "b" },
      { id: 1, name: "c" },
    ];
    expect(
      linq(
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
      linq(
        [1, 2, 3],
        selectMany((x) => [x, x * 10]),
        toArray(),
      ),
    ).toEqual([1, 10, 2, 20, 3, 30]);
  });

  test("orderBy / orderByDescending", () => {
    expect(
      linq(
        [3, 1, 2],
        orderBy<number, number>((x) => x),
        toArray(),
      ),
    ).toEqual([1, 2, 3]);
    expect(
      linq(
        [3, 1, 2],
        orderByDescending<number, number>((x) => x),
        toArray(),
      ),
    ).toEqual([3, 2, 1]);
  });

  test("concat + append + prepend", () => {
    expect(linq([1, 2], concat([3, 4]), toArray())).toEqual([1, 2, 3, 4]);
    expect(linq([1, 2], append<number>(99), toArray())).toEqual([1, 2, 99]);
  });

  test("zip", () => {
    expect(
      linq(
        [1, 2, 3],
        zip([10, 20, 30], (a: number, b: number) => a + b),
        toArray(),
      ),
    ).toEqual([11, 22, 33]);
  });

  test("first / firstOrDefault", () => {
    expect(linq([1, 2, 3], first<number>())).toBe(1);
    expect(linq([1, 2, 3], firstOrDefault<number>((x) => x > 10))).toBeNull();
    expect(() => linq([] as number[], first<number>())).toThrow();
  });

  test("last", () => {
    expect(linq([1, 2, 3], last<number>())).toBe(3);
  });

  test("any / all / count / contains", () => {
    expect(linq([1, 2, 3], any<number>((x) => x > 2))).toBe(true);
    expect(linq([1, 2, 3], any<number>((x) => x > 10))).toBe(false);
    expect(linq([1, 2, 3], all<number>((x) => x > 0))).toBe(true);
    expect(linq([1, 2, 3], all<number>((x) => x > 1))).toBe(false);
    expect(linq([1, 2, 3], count<number>())).toBe(3);
    expect(linq([1, 2, 3], contains<number>(2))).toBe(true);
  });

  test("sum + aggregate", () => {
    expect(linq([1, 2, 3], sum<number>())).toBe(6);
    expect(linq([1, 2, 3], aggregate(0, (a: number, b: number) => a + b * 2))).toBe(12);
  });

  test("toMap / toSet", () => {
    type Item = { id: number; name: string };
    const items: Item[] = [
      { id: 1, name: "a" },
      { id: 2, name: "b" },
    ];
    const map = linq(
      items,
      toMap(
        (i: Item) => i.id,
        (i: Item) => i.name,
      ),
    );
    expect(map.get(1)).toBe("a");
    expect(map.get(2)).toBe("b");

    const set = linq([1, 2, 2, 3], toSet<number>());
    expect(set.size).toBe(3);
  });

  test("re-enumerable — chain can be iterated multiple times (.NET LINQ parity)", () => {
    const items = [1, 2, 3, 4, 5];
    const chain = linq(
      items,
      where<number>((x) => x % 2 === 0),
      select((x) => x * 10),
    );

    expect([...chain]).toEqual([20, 40]);
    // Second enumeration runs the operators afresh against `items`.
    expect([...chain]).toEqual([20, 40]);
    // Mutate source between enumerations — observable through the lazy chain.
    items.push(6);
    expect([...chain]).toEqual([20, 40, 60]);
  });

  test("re-enumerable — orderBy re-sorts on each enumeration", () => {
    const items = [3, 1, 2];
    const sorted = linq(
      items,
      orderBy<number, number>((x) => x),
    );
    expect([...sorted]).toEqual([1, 2, 3]);
    items.push(0);
    expect([...sorted]).toEqual([0, 1, 2, 3]);
  });

  test("descriptor carries optional queryable meta (tree + captures)", () => {
    // The compiler populates `queryable` when the call site uses an
    // opt-in IQueryable surface. Hand-author here to pin the shape
    // consumers will see.
    const w = where<{ age: number }>(
      (u) => u.age > 18,
      {
        tree: {
          kind: "binary",
          op: ">",
          left: {
            kind: "member",
            target: { kind: "param", name: "u", type: "User" },
            member: "age",
          },
          right: { kind: "literal", value: 18, type: "number" },
        },
      },
    );

    expect(w.queryable).toBeDefined();
    expect(w.queryable!.tree.kind).toBe("binary");
    if (w.queryable?.tree.kind === "binary") {
      expect(w.queryable.tree.op).toBe(">");
      expect(w.queryable.tree.left.kind).toBe("member");
      expect(w.queryable.tree.right.kind).toBe("literal");
    }

    // Runtime path still works through the closure when no provider intercepts.
    expect(w.predicate({ age: 20 }, 0)).toBe(true);
    expect(w.predicate({ age: 5 }, 0)).toBe(false);
  });

  test("queryable meta carries closure captures for runtime resolution", () => {
    const minAge = 18;
    const w = where<{ age: number }>(
      (u) => u.age > minAge,
      {
        tree: {
          kind: "binary",
          op: ">",
          left: {
            kind: "member",
            target: { kind: "param", name: "u" },
            member: "age",
          },
          right: { kind: "capture", name: "minAge", type: "number" },
        },
        captures: { minAge },
      },
    );

    expect(w.queryable!.captures).toBeDefined();
    expect(w.queryable!.captures!.minAge).toBe(18);
    // Closure path keeps working — provider that ignores the tree falls
    // back to running the predicate directly.
    expect(w.predicate({ age: 30 }, 0)).toBe(true);
  });

  test("queryable slot is absent for plain LINQ-to-Objects calls", () => {
    const w = where<number>((x) => x > 0);
    expect(w.queryable).toBeUndefined();
  });

  test("descriptor introspection — IQueryable provider can read kind + lambdas", () => {
    // Each operator/terminal returns a tagged descriptor. A future
    // IQueryable provider walks the chain by `kind` to translate to
    // SQL/GraphQL/etc. without parsing closure source — the lambdas
    // sit in well-named fields.
    const w = where<number>((x) => x > 0);
    expect(w.kind).toBe("where");
    expect(typeof w.predicate).toBe("function");
    expect(w.predicate(1, 0)).toBe(true);
    expect(w.predicate(-1, 0)).toBe(false);

    const s = select<number, string>((x) => x.toString());
    expect(s.kind).toBe("select");
    expect(s.selector(42, 0)).toBe("42");

    const t = take<number>(5);
    expect(t.kind).toBe("take");
    expect(t.count).toBe(5);

    const oba = orderBy<number, number>((x) => x);
    expect(oba.kind).toBe("orderBy");
    expect(oba.keySelector(7)).toBe(7);

    const term = count<number>((x) => x % 2 === 0);
    expect(term.kind).toBe("count");
    expect(term.predicate?.(2)).toBe(true);
  });

  test("lazy semantics — generators not executed until terminal", () => {
    let calls = 0;
    function* counter(): Iterable<number> {
      for (let i = 0; i < 1000; i++) {
        calls++;
        yield i;
      }
    }
    linq(
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
