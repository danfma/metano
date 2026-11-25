import { Dictionary, HashSet, List, type IEqualityComparer } from "metano-runtime/system/collections";
import { aggregate, all, any, append, average, averageDecimal, averageNullable, averageNullableDecimal, concat, contains, count, defaultIfEmpty, distinct, distinctBy, elementAt, elementAtOrDefault, except, first, firstOrDefault, groupBy, intersect, join, last, lastOrDefault, linq, max, maxBy, maxNullable, min, minBy, minNullable, orderBy, orderByDescending, prepend, reverse, select, selectMany, sequenceEqual, single, singleOrDefault, skip, skipWhile, sum, sumDecimal, take, takeWhile, thenBy, thenByDescending, toArray, toDictionary, toHashSet, toList, toLookup, union, where, zip } from "metano-runtime/system/linq";
import { Decimal } from "metano-runtime/system/decimal";

export class Supported {
  constructor() { }

  where(xs: List<number>): Object {
    return linq(xs, where((x: number, i: any) => ((x: number) => x > 1)(x) && ((x: number, i: number) => i > 0)(x, i)));
  }

  select(xs: List<number>): Object {
    return linq(xs, select((x: number) => Math.imul(x, 2)), select((x: number, i: number) => (x + i) | 0));
  }

  selectMany(xss: List<List<number>>): Object {
    return linq(xss, selectMany((x: List<number>) => x));
  }

  ascending(xs: List<number>): Object {
    return linq(xs, orderBy((x: number) => x), thenBy((x: number) => -x | 0));
  }

  descending(xs: List<number>): Object {
    return linq(xs, orderByDescending((x: number) => x), thenByDescending((x: number) => -x | 0));
  }

  groupBy(xs: List<number>): Object {
    return linq(xs, groupBy((x: number) => x % 2));
  }

  groupByComparer(xs: List<number>, same: IEqualityComparer<number>): Object {
    return linq(xs, groupBy((x: number) => x % 2, same));
  }

  join(xs: List<number>, ys: List<number>, same: IEqualityComparer<number>): Object {
    return linq(xs, join(ys, (o: number) => o, (i: number) => i, (o: number, i: number) => (o + i) | 0), join(ys, (o: number) => o, (i: number) => i, (o: number, i: number) => Math.imul(o, i), same));
  }

  paging(xs: List<number>): Object {
    return linq(xs, skip(1), take(2), skipWhile((x: number) => x < 1), takeWhile((x: number) => x < 5));
  }

  distinct(xs: List<number>, same: IEqualityComparer<number>): Object {
    return linq(xs, distinct(), distinct(same), distinctBy((x: number) => x % 3));
  }

  defaultIfEmpty(xs: List<number>, ss: List<string>): Object {
    return linq(xs, defaultIfEmpty(0), defaultIfEmpty(7), concat(xs), concat(linq(ss, defaultIfEmpty(""), select((s: string) => s.length))), concat([linq(ss, defaultIfEmpty(), count())]));
  }

  concat(xs: List<number>, ys: List<number>): Object {
    return linq(xs, concat(ys), append(4), prepend(0), reverse());
  }

  setOperations(xs: List<number>, ys: List<number>): Object {
    return linq(xs, union(ys), intersect(ys), except(ys));
  }

  staticForm(xs: List<number>): Object {
    return (linq(xs, where((x: number) => x > 1), count()) + linq(xs, count())) | 0;
  }

  zip(xs: List<number>, ys: List<number>): Object {
    return linq(xs, zip(ys, (a: number, b: number) => (a + b) | 0));
  }

  toArray(xs: List<number>): number[] {
    return xs.toArray();
  }

  toArrayOfSequence(xs: Iterable<number>): number[] {
    return linq(xs, toArray());
  }

  toList(xs: List<number>): List<number> {
    return linq(xs, toList());
  }

  toDictionary(xs: List<number>): Dictionary<number, number> {
    return linq(xs, toDictionary((x: number) => x));
  }

  toDictionaryElement(xs: List<number>): Dictionary<number, number> {
    return linq(xs, toDictionary((x: number) => x, (x: number) => Math.imul(x, 2)));
  }

  toHashSet(xs: List<number>): HashSet<number> {
    return linq(xs, toHashSet());
  }

  firsts(xs: List<number>): number {
    return (linq(xs, first()) + linq(xs, first((x: number) => x > 1)) + linq(xs, last()) + linq(xs, last((x: number) => x > 1)) + linq(xs, single()) + linq(xs, single((x: number) => x > 1))) | 0;
  }

  orDefaults(xs: List<number>): number {
    return (linq(xs, firstOrDefault(undefined, 0)) + linq(xs, firstOrDefault((x: number) => x > 1, 0)) + linq(xs, lastOrDefault(undefined, 7)) + linq(xs, singleOrDefault((x: number) => x > 1, 7))) | 0;
  }

  firstOrDefaults(xs: List<number>): number {
    return (linq(xs, firstOrDefault(undefined, 7)) + linq(xs, firstOrDefault((x: number) => x > 1, 7))) | 0;
  }

  lastOrDefaults(xs: List<number>): number {
    return (linq(xs, lastOrDefault(undefined, 0)) + linq(xs, lastOrDefault((x: number) => x > 1, 0)) + linq(xs, lastOrDefault((x: number) => x > 1, 7))) | 0;
  }

  singleOrDefaults(xs: List<number>): number {
    return (linq(xs, singleOrDefault(undefined, 0)) + linq(xs, singleOrDefault((x: number) => x > 1, 0)) + linq(xs, singleOrDefault(undefined, 7))) | 0;
  }

  quantifiers(xs: List<number>): boolean {
    return linq(xs, any()) && linq(xs, any((x: number) => x > 1)) && linq(xs, all((x: number) => x > 0));
  }

  counts(xs: List<number>): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, BigInt((linq(xs, count()) + linq(xs, count((x: number) => x > 1))) | 0) + BigInt(linq(xs, count()))) + BigInt(linq(xs, count((x: number) => x > 1))));
  }

  numbers(xs: List<number>): number {
    return ((linq(xs, sum()) + linq(xs, sum((x: number) => Math.imul(x, 2))) + linq(xs, min()) + linq(xs, min((x: number) => -x | 0)) + linq(xs, max()) + linq(xs, max((x: number) => -x | 0))) | 0) + linq(xs, average()) + linq(xs, average((x: number) => Math.imul(x, 2)));
  }

  decimals(ms: List<Decimal>, nms: List<Decimal | null>, ss: List<string>, ns: List<number | null>): Object {
    return [linq(ms, sumDecimal(Decimal)), linq(ms, averageDecimal()), linq(ms, min((m: Decimal) => m.neg())), linq(nms, sumDecimal(Decimal)), linq(nms, averageNullableDecimal()), linq(ss, minNullable()), linq(ss, maxNullable()), linq(ns, minNullable()), linq(ns, averageNullable())];
  }

  extremes(xs: List<number>): number {
    return (linq(xs, minBy((x: number) => -x | 0)) + linq(xs, maxBy((x: number) => -x | 0))) | 0;
  }

  contains(xs: Iterable<number>): boolean {
    return linq(xs, contains(3));
  }

  containsComparer(xs: Iterable<number>, same: IEqualityComparer<number>): boolean {
    return linq(xs, contains(3, same));
  }

  sequenceEquals(xs: List<number>, ys: List<number>, same: IEqualityComparer<number>): boolean {
    return linq(xs, sequenceEqual(ys)) && linq(xs, sequenceEqual(ys, same));
  }

  toLookups(xs: List<number>, same: IEqualityComparer<number>): Object {
    return [linq(xs, toLookup((x: number) => x)), linq(xs, toLookup((x: number) => x, same)), linq(xs, toLookup((x: number) => x, (x: number) => Math.imul(x, 2))), linq(xs, toLookup((x: number) => x, (x: number) => Math.imul(x, 2), same))];
  }

  aggregate(xs: List<number>): number {
    return linq(xs, aggregate(0, (a: number, x: number) => (a + x) | 0));
  }

  elementAts(xs: List<number>, ss: List<string>): number {
    return (linq(xs, elementAt(1)) + linq(xs, elementAtOrDefault(1, 0)) + linq(ss, elementAtOrDefault(2))!.length) | 0;
  }

  queryable(q: Iterable<number>, ys: List<number>): Object {
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
    }), select((x: number) => Math.imul(x, 2), {
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
    }), concat(ys), union(ys), intersect(ys), except(ys), zip(ys, (a: number, b: number) => (a + b) | 0, {
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

  queryableToDictionary(q: Iterable<number>): Dictionary<number, number> {
    return linq(q, toDictionary((x: number) => x, (x: number) => Math.imul(x, 2), {
      tree: {
        kind: "param",
        name: "x",
        type: { name: "number" },
      },
    }));
  }

  queryableTerminals(q: Iterable<number>): number {
    return (linq(q, first((x: number) => x > 1, {
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
    })) + (linq(q, contains(3)) ? 1 : 0)) | 0;
  }

  queryableSearches(q: Iterable<number>, ys: List<number>, same: IEqualityComparer<number>): boolean {
    return linq(q, contains(3, same)) && linq(q, sequenceEqual(ys)) && linq(q, sequenceEqual(ys, same));
  }
}
