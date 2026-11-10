import { HashSet } from "metano-runtime";
import { aggregate, all, any, append, average, concat, contains, count, distinct, distinctBy, first, firstOrDefault, groupBy, last, lastOrDefault, linq, max, maxBy, min, minBy, orderBy, orderByDescending, prepend, reverse, select, selectMany, single, singleOrDefault, skip, skipWhile, sum, take, takeWhile, thenBy, thenByDescending, toArray, toMap, toSet, where, zip } from "metano-runtime/system/linq";

export class Supported {
  constructor() { }

  where(xs: number[]): Object {
    return linq(xs, where((x: number, i: any) => ((x: number) => x > 1)(x) && ((x: number, i: number) => i > 0)(x, i)));
  }

  select(xs: number[]): Object {
    return linq(xs, select((x: number) => x * 2), select((x: number, i: number) => x + i));
  }

  selectMany(xss: number[][]): Object {
    return linq(xss, selectMany((x: number[]) => x));
  }

  ascending(xs: number[]): Object {
    return linq(xs, orderBy((x: number) => x), thenBy((x: number) => -x));
  }

  descending(xs: number[]): Object {
    return linq(xs, orderByDescending((x: number) => x), thenByDescending((x: number) => -x));
  }

  groupBy(xs: number[]): Object {
    return linq(xs, groupBy((x: number) => x % 2));
  }

  paging(xs: number[]): Object {
    return linq(xs, skip(1), take(2), skipWhile((x: number) => x < 1), takeWhile((x: number) => x < 5));
  }

  distinct(xs: number[]): Object {
    return linq(xs, distinct(), distinctBy((x: number) => x % 3));
  }

  concat(xs: number[], ys: number[]): Object {
    return linq(xs, concat(ys), append(4), prepend(0), reverse());
  }

  zip(xs: number[], ys: number[]): Object {
    return linq(xs, zip(ys, (a: number, b: number) => a + b));
  }

  toArray(xs: number[]): number[] {
    return xs.slice();
  }

  toList(xs: number[]): number[] {
    return linq(xs, toArray());
  }

  toDictionary(xs: number[]): Map<number, number> {
    return linq(xs, toMap((x: number) => x));
  }

  toDictionaryElement(xs: number[]): Map<number, number> {
    return linq(xs, toMap((x: number) => x, (x: number) => x * 2));
  }

  toHashSet(xs: number[]): HashSet<number> {
    return linq(xs, toSet());
  }

  firsts(xs: number[]): number {
    return linq(xs, first()) + linq(xs, first((x: number) => x > 1)) + linq(xs, last()) + linq(xs, last((x: number) => x > 1)) + linq(xs, single()) + linq(xs, single((x: number) => x > 1));
  }

  orDefaults(xs: number[]): number {
    return linq(xs, firstOrDefault(undefined, 0)) + linq(xs, firstOrDefault((x: number) => x > 1, 0)) + linq(xs, lastOrDefault(undefined, 7)) + linq(xs, singleOrDefault((x: number) => x > 1, 7));
  }

  quantifiers(xs: number[]): boolean {
    return linq(xs, any()) && linq(xs, any((x: number) => x > 1)) && linq(xs, all((x: number) => x > 0));
  }

  counts(xs: number[]): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, BigInt(linq(xs, count()) + linq(xs, count((x: number) => x > 1))) + BigInt(linq(xs, count()))) + BigInt(linq(xs, count((x: number) => x > 1))));
  }

  numbers(xs: number[]): number {
    return linq(xs, sum()) + linq(xs, sum((x: number) => x * 2)) + linq(xs, min()) + linq(xs, min((x: number) => -x)) + linq(xs, max()) + linq(xs, max((x: number) => -x)) + linq(xs, average()) + linq(xs, average((x: number) => x * 2));
  }

  extremes(xs: number[]): number {
    return linq(xs, minBy((x: number) => -x)) + linq(xs, maxBy((x: number) => -x));
  }

  contains(xs: Iterable<number>): boolean {
    return linq(xs, contains(3));
  }

  aggregate(xs: number[]): number {
    return linq(xs, aggregate(0, (a: number, x: number) => a + x));
  }

  queryable(q: Iterable<number>, ys: number[]): Object {
    return linq(q, where((x: number) => x > 1, {
      tree: {
        kind: "binary",
        op: ">",
        left: {
          kind: "param",
          name: "x",
          type: { name: "number" },
        },
        right: {
          kind: "literal",
          value: 1,
          type: { name: "number" },
        },
      },
    }), orderBy((x: number) => x, {
      tree: {
        kind: "param",
        name: "x",
        type: { name: "number" },
      },
    }), select((x: number) => x * 2, {
      tree: {
        kind: "binary",
        op: "*",
        left: {
          kind: "param",
          name: "x",
          type: { name: "number" },
        },
        right: {
          kind: "literal",
          value: 2,
          type: { name: "number" },
        },
      },
    }), concat(ys), zip(ys, (a: number, b: number) => a + b, {
      tree: {
        kind: "binary",
        op: "+",
        left: {
          kind: "param",
          name: "a",
          type: { name: "number" },
        },
        right: {
          kind: "param",
          name: "b",
          type: { name: "number" },
        },
      },
    }));
  }

  queryableToDictionary(q: Iterable<number>): Map<number, number> {
    return linq(q, toMap((x: number) => x, (x: number) => x * 2, {
      tree: {
        kind: "param",
        name: "x",
        type: { name: "number" },
      },
    }));
  }

  queryableTerminals(q: Iterable<number>): number {
    return linq(q, first((x: number) => x > 1, {
      tree: {
        kind: "binary",
        op: ">",
        left: {
          kind: "param",
          name: "x",
          type: { name: "number" },
        },
        right: {
          kind: "literal",
          value: 1,
          type: { name: "number" },
        },
      },
    })) + linq(q, firstOrDefault((x: number) => x > 1, 7, {
      tree: {
        kind: "binary",
        op: ">",
        left: {
          kind: "param",
          name: "x",
          type: { name: "number" },
        },
        right: {
          kind: "literal",
          value: 1,
          type: { name: "number" },
        },
      },
    })) + linq(q, count((x: number) => x > 1, {
      tree: {
        kind: "binary",
        op: ">",
        left: {
          kind: "param",
          name: "x",
          type: { name: "number" },
        },
        right: {
          kind: "literal",
          value: 1,
          type: { name: "number" },
        },
      },
    })) + linq(q, sum((x: number) => x, {
      tree: {
        kind: "param",
        name: "x",
        type: { name: "number" },
      },
    })) + (linq(q, contains(3)) ? 1 : 0);
  }
}
