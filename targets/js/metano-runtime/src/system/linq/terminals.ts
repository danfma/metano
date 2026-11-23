/**
 * Terminal factories — each returns a tagged descriptor whose `apply`
 * walks the source and produces a concrete value (array, scalar,
 * boolean). Same descriptor shape as composition operators so an
 * IQueryable provider can switch over the terminal kind exhaustively
 * (e.g. translate `count` to `SELECT COUNT(*)`, `sum` to
 * `SELECT SUM(<selector>)`, etc.).
 *
 * Short-circuit terminals (`first`, `any`, `contains`) stop iterating
 * as soon as the answer is known.
 */
import { Dictionary } from "../collections/dictionary.ts";
import { HashSet } from "../collections/hash-set.ts";
import { throwOOR } from "../collections/throw-helpers.ts";
import { List } from "../collections/list.ts";
import { ArgumentOutOfRangeException } from "../exceptions/argument-out-of-range-exception.ts";
import { InvalidOperationException } from "../exceptions/invalid-operation-exception.ts";
import type { RuntimeType } from "../reflection/runtime-type.ts";
import { compareKeys } from "./compare-keys.ts";
import type { QueryableMeta } from "./expr-tree.ts";
import type {
  AggregateTerm,
  AllTerm,
  AnyTerm,
  AverageTerm,
  ContainsTerm,
  CountTerm,
  ElementAtOrDefaultTerm,
  ElementAtTerm,
  FirstOrDefaultTerm,
  FirstTerm,
  LastOrDefaultTerm,
  LastTerm,
  MaxByTerm,
  MaxTerm,
  MinByTerm,
  MinTerm,
  SingleOrDefaultTerm,
  SingleTerm,
  SumTerm,
  ToArrayTerm,
  ToDictionaryTerm,
  ToHashSetTerm,
  ToListTerm,
  ToMapTerm,
  ToSetTerm,
} from "./types.ts";

export function toArray<T>(): ToArrayTerm<T> {
  return {
    kind: "toArray",
    apply: (source) => [...source],
  };
}

export function toMap<T, K, V = T>(
  keySelector: (item: T) => K,
  valueSelector: (item: T) => V = ((item: T) => item) as unknown as (item: T) => V,
  keyQueryable?: QueryableMeta,
  valueQueryable?: QueryableMeta,
): ToMapTerm<T, K, V> {
  return {
    kind: "toMap",
    keySelector,
    valueSelector,
    keyQueryable,
    valueQueryable,
    apply: (source) => {
      const m = new Map<K, V>();
      for (const item of source) m.set(keySelector(item), valueSelector(item));
      return m;
    },
  };
}

export function toSet<T>(): ToSetTerm<T> {
  return {
    kind: "toSet",
    apply: (source) => new HashSet(source),
  };
}

/** `ToList()`: a new runtime `List` holding the elements in order (spec 011 § LINQ terminals). */
export function toList<T>(typeArguments?: readonly RuntimeType[] | null): ToListTerm<T> {
  return {
    kind: "toList",
    apply: (source) => List.of([...source], typeArguments),
  };
}

/**
 * `ToDictionary(keySelector[, elementSelector])`: a new runtime `Dictionary`;
 * a repeated key throws `ArgumentException`, as .NET's `ToDictionary` does.
 */
export function toDictionary<T, K>(
  keySelector: (item: T) => K,
  valueSelector?: undefined,
  keyQueryable?: QueryableMeta,
  valueQueryable?: undefined,
  typeArguments?: readonly RuntimeType[] | null,
): ToDictionaryTerm<T, K, T>;
export function toDictionary<T, K, V>(
  keySelector: (item: T) => K,
  valueSelector: (item: T) => V,
  keyQueryable?: QueryableMeta,
  valueQueryable?: QueryableMeta,
  typeArguments?: readonly RuntimeType[] | null,
): ToDictionaryTerm<T, K, V>;
export function toDictionary<T, K, V>(
  keySelector: (item: T) => K,
  valueSelector?: (item: T) => V,
  keyQueryable?: QueryableMeta,
  valueQueryable?: QueryableMeta,
  typeArguments?: readonly RuntimeType[] | null,
): ToDictionaryTerm<T, K, V | T> {
  const selectValue: (item: T) => V | T = valueSelector ?? ((item) => item);

  function materialize(source: Iterable<T>): Dictionary<K, V | T> {
    const dictionary = new Dictionary<K, V | T>(null, null, typeArguments);

    for (const item of source) {
      dictionary.add(keySelector(item), selectValue(item));
    }

    return dictionary;
  }

  return {
    kind: "toDictionary",
    keySelector,
    valueSelector: selectValue,
    keyQueryable,
    valueQueryable,
    apply: materialize,
  };
}

/** `ToHashSet()`: a new runtime `HashSet`; duplicates collapse. */
export function toHashSet<T>(typeArguments?: readonly RuntimeType[] | null): ToHashSetTerm<T> {
  return {
    kind: "toHashSet",
    apply: (source) => HashSet.from(source, null, typeArguments),
  };
}

export function first<T>(
  predicate?: (item: T) => boolean,
  queryable?: QueryableMeta,
): FirstTerm<T> {
  return {
    kind: "first",
    predicate,
    queryable,
    apply: (source) => {
      for (const item of source) if (!predicate || predicate(item)) return item;
      throw noElementException(predicate != null);
    },
  };
}

/**
 * `FirstOrDefault`: without a `defaultValue` a miss is `null`; with one, the result is a `T`
 * (or `T | null` for a nullable default). `T` comes from the surrounding `linq(...)` call,
 * never from the default: write `firstOrDefault<number>(undefined, 0)` to build a term standalone.
 */
export function firstOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue?: null,
  queryable?: QueryableMeta,
): FirstOrDefaultTerm<T, null>;
export function firstOrDefault<T>(
  predicate: ((item: T) => boolean) | undefined,
  defaultValue: NoInfer<T>,
  queryable?: QueryableMeta,
): FirstOrDefaultTerm<T, T>;
export function firstOrDefault<T>(
  predicate: ((item: T) => boolean) | undefined,
  defaultValue: NoInfer<T> | null | undefined,
  queryable?: QueryableMeta,
): FirstOrDefaultTerm<T, T | null>;
export function firstOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue: T | null = null,
  queryable?: QueryableMeta,
): FirstOrDefaultTerm<T> {
  return {
    kind: "firstOrDefault",
    predicate,
    defaultValue,
    queryable,
    apply: (source) => {
      for (const item of source) if (!predicate || predicate(item)) return item;
      return defaultValue;
    },
  };
}

/**
 * `ElementAt(index)`.
 *
 * @throws ArgumentOutOfRangeException when `index` is negative or past the last element.
 */
export function elementAt<T>(index: number): ElementAtTerm<T> {
  return {
    kind: "elementAt",
    index,
    apply: (source) => {
      const found = elementOf(source, index);

      if (found === undefined) return throwIndexOutOfRange(source);

      return found.value;
    },
  };
}

/**
 * `ElementAtOrDefault(index)`: without a `defaultValue` a miss is `null`; with one, the
 * result is a `T`. `T` comes from the surrounding `linq(...)` call.
 */
export function elementAtOrDefault<T>(
  index: number,
  defaultValue?: null,
): ElementAtOrDefaultTerm<T, null>;
export function elementAtOrDefault<T>(
  index: number,
  defaultValue: NoInfer<T>,
): ElementAtOrDefaultTerm<T, T>;
export function elementAtOrDefault<T>(
  index: number,
  defaultValue: T | null = null,
): ElementAtOrDefaultTerm<T> {
  return {
    kind: "elementAtOrDefault",
    index,
    defaultValue,
    apply: (source) => {
      const found = elementOf(source, index);

      return found === undefined ? defaultValue : found.value;
    },
  };
}

/**
 * An index outside the source. An array or a `List` (`IList<T>` in .NET) reports the
 * collection message, any other sequence the generic one.
 */
function throwIndexOutOfRange(source: Iterable<unknown>): never {
  if (Array.isArray(source) || source instanceof List) {
    return throwOOR();
  }

  throw new ArgumentOutOfRangeException("index");
}

/** The element at `index`, boxed so an element that is `undefined` or `null` is told from a miss. */
function elementOf<T>(source: Iterable<T>, index: number): { readonly value: T } | undefined {
  if (index < 0) return undefined;

  let position = 0;

  for (const item of source) {
    if (position === index) return { value: item };
    position++;
  }

  return undefined;
}

export function last<T>(predicate?: (item: T) => boolean, queryable?: QueryableMeta): LastTerm<T> {
  return {
    kind: "last",
    predicate,
    queryable,
    apply: (source) => {
      let result: T | undefined;
      let found = false;
      for (const item of source)
        if (!predicate || predicate(item)) {
          result = item;
          found = true;
        }
      if (!found) throw noElementException(predicate != null);
      return result!;
    },
  };
}

/**
 * `LastOrDefault`: without a `defaultValue` a miss is `null`; with one, the result is a `T`
 * (or `T | null` for a nullable default). `T` comes from the surrounding `linq(...)` call,
 * never from the default: write `lastOrDefault<number>(undefined, 0)` to build a term standalone.
 */
export function lastOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue?: null,
  queryable?: QueryableMeta,
): LastOrDefaultTerm<T, null>;
export function lastOrDefault<T>(
  predicate: ((item: T) => boolean) | undefined,
  defaultValue: NoInfer<T>,
  queryable?: QueryableMeta,
): LastOrDefaultTerm<T, T>;
export function lastOrDefault<T>(
  predicate: ((item: T) => boolean) | undefined,
  defaultValue: NoInfer<T> | null | undefined,
  queryable?: QueryableMeta,
): LastOrDefaultTerm<T, T | null>;
export function lastOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue: T | null = null,
  queryable?: QueryableMeta,
): LastOrDefaultTerm<T> {
  return {
    kind: "lastOrDefault",
    predicate,
    defaultValue,
    queryable,
    apply: (source) => {
      let result: T | null = defaultValue;
      for (const item of source) if (!predicate || predicate(item)) result = item;
      return result;
    },
  };
}

/**
 * `SingleOrDefault`: without a `defaultValue` a miss is `null`; with one, the result is a `T`
 * (or `T | null` for a nullable default). `T` comes from the surrounding `linq(...)` call,
 * never from the default: write `singleOrDefault<number>(undefined, 0)` to build a term standalone.
 */
export function singleOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue?: null,
  queryable?: QueryableMeta,
): SingleOrDefaultTerm<T, null>;
export function singleOrDefault<T>(
  predicate: ((item: T) => boolean) | undefined,
  defaultValue: NoInfer<T>,
  queryable?: QueryableMeta,
): SingleOrDefaultTerm<T, T>;
export function singleOrDefault<T>(
  predicate: ((item: T) => boolean) | undefined,
  defaultValue: NoInfer<T> | null | undefined,
  queryable?: QueryableMeta,
): SingleOrDefaultTerm<T, T | null>;
export function singleOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue: T | null = null,
  queryable?: QueryableMeta,
): SingleOrDefaultTerm<T> {
  return {
    kind: "singleOrDefault",
    predicate,
    defaultValue,
    queryable,
    apply: (source) => {
      let result: T | null = defaultValue;
      let count = 0;
      for (const item of source)
        if (!predicate || predicate(item)) {
          result = item;
          if (++count > 1) throw moreThanOneElementException(predicate != null);
        }
      return result;
    },
  };
}

export function single<T>(
  predicate?: (item: T) => boolean,
  queryable?: QueryableMeta,
): SingleTerm<T> {
  return {
    kind: "single",
    predicate,
    queryable,
    apply: (source) => {
      let result: T | undefined;
      let count = 0;
      for (const item of source)
        if (!predicate || predicate(item)) {
          result = item;
          if (++count > 1) throw moreThanOneElementException(predicate != null);
        }
      if (count === 0) throw noElementException(predicate != null);
      return result!;
    },
  };
}

export function any<T>(predicate?: (item: T) => boolean, queryable?: QueryableMeta): AnyTerm<T> {
  return {
    kind: "any",
    predicate,
    queryable,
    apply: (source) => {
      for (const item of source) if (!predicate || predicate(item)) return true;
      return false;
    },
  };
}

export function all<T>(predicate: (item: T) => boolean, queryable?: QueryableMeta): AllTerm<T> {
  return {
    kind: "all",
    predicate,
    queryable,
    apply: (source) => {
      for (const item of source) if (!predicate(item)) return false;
      return true;
    },
  };
}

export function count<T>(
  predicate?: (item: T) => boolean,
  queryable?: QueryableMeta,
): CountTerm<T> {
  return {
    kind: "count",
    predicate,
    queryable,
    apply: (source) => {
      let n = 0;
      for (const item of source) if (!predicate || predicate(item)) n++;
      return n;
    },
  };
}

/**
 * `Sum()` / `Sum(selector)` over `int`, `double`, … and their nullable forms: nulls count as
 * zero, an empty sequence is `0`. The `decimal` and 64-bit sums are `sumDecimal` and `sumInt64`.
 */
export function sum<T>(
  selector?: (item: T) => number | null,
  queryable?: QueryableMeta,
): SumTerm<T> {
  return {
    kind: "sum",
    selector,
    queryable,
    apply: (source) => {
      let total = 0;
      for (const item of source)
        total += (selector ? selector(item) : (item as number | null)) ?? 0;
      return total;
    },
  };
}

/**
 * `Min()` / `Min(selector)` over a sequence of non-nullable values: numbers, strings, `decimal`s,
 * dates, anything ordered. Nulls are skipped; a `NaN` wins, as in .NET.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function min<T, R = T>(selector?: (item: T) => R, queryable?: QueryableMeta): MinTerm<T, R> {
  return {
    kind: "min",
    selector,
    queryable,
    apply: (source) => requireValue(smallest(valuesOf(source, selector))) as R,
  };
}

/**
 * `Min()` / `Min(selector)` over `int?`, `string`, a class, …: nulls are skipped and
 * `null` is the result when no value is left.
 */
export function minNullable<T, R = T>(
  selector?: (item: T) => R | null,
  queryable?: QueryableMeta,
): MinTerm<T, NonNullable<R> | null> {
  return {
    kind: "min",
    selector,
    queryable,
    apply: (source) => (smallest(valuesOf(source, selector)) ?? null) as NonNullable<R> | null,
  };
}

/**
 * `Max()` / `Max(selector)` over a sequence of non-nullable values. Nulls are skipped; a `NaN`
 * loses to any other value, as in .NET.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function max<T, R = T>(selector?: (item: T) => R, queryable?: QueryableMeta): MaxTerm<T, R> {
  return {
    kind: "max",
    selector,
    queryable,
    apply: (source) => requireValue(largest(valuesOf(source, selector))) as R,
  };
}

/** `Max()` / `Max(selector)` over `int?`, `string`, a class, …: `null` when no value is left. */
export function maxNullable<T, R = T>(
  selector?: (item: T) => R | null,
  queryable?: QueryableMeta,
): MaxTerm<T, NonNullable<R> | null> {
  return {
    kind: "max",
    selector,
    queryable,
    apply: (source) => (largest(valuesOf(source, selector)) ?? null) as NonNullable<R> | null,
  };
}

/**
 * `MinBy(keySelector)` over a value-typed element: the first element with the smallest key.
 * A `null` key is skipped; when every key is null the first element is the result.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function minBy<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): MinByTerm<T, K> {
  return {
    kind: "minBy",
    keySelector,
    queryable,
    apply: (source) => requireValue(extremeBy(source, keySelector, -1)),
  };
}

/** `MinBy(keySelector)` over a reference or nullable element: `null` for an empty sequence. */
export function minByNullable<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): MinByTerm<T, K, T | null> {
  return {
    kind: "minBy",
    keySelector,
    queryable,
    apply: (source) => extremeBy(source, keySelector, -1) ?? null,
  };
}

/**
 * `MaxBy(keySelector)` over a value-typed element: the first element with the largest key.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function maxBy<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): MaxByTerm<T, K> {
  return {
    kind: "maxBy",
    keySelector,
    queryable,
    apply: (source) => requireValue(extremeBy(source, keySelector, 1)),
  };
}

/** `MaxBy(keySelector)` over a reference or nullable element: `null` for an empty sequence. */
export function maxByNullable<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): MaxByTerm<T, K, T | null> {
  return {
    kind: "maxBy",
    keySelector,
    queryable,
    apply: (source) => extremeBy(source, keySelector, 1) ?? null,
  };
}

/**
 * `Average()` / `Average(selector)` over `int`, `double`, …: the sum as a `number`, divided by
 * the count. The `decimal` and 64-bit averages are `averageDecimal` and `averageInt64`.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function average<T>(
  selector?: (item: T) => number,
  queryable?: QueryableMeta,
): AverageTerm<T> {
  return {
    kind: "average",
    selector,
    queryable,
    apply: (source) => requireValue(averageOf(valuesOf(source, selector) as Iterable<number>)),
  };
}

/** `Average()` / `Average(selector)` over `int?`, `double?`, …: nulls skipped; `null` when none is left. */
export function averageNullable<T>(
  selector?: (item: T) => number | null,
  queryable?: QueryableMeta,
): AverageTerm<T, number | null> {
  return {
    kind: "average",
    selector,
    queryable,
    apply: (source) => averageOf(valuesOf(source, selector) as Iterable<number>),
  };
}

export function contains<T>(value: T): ContainsTerm<T> {
  return {
    kind: "contains",
    value,
    apply: (source) => {
      for (const element of source) if (element === value) return true;
      return false;
    },
  };
}

export function aggregate<T, A>(seed: A, accumulator: (acc: A, item: T) => A): AggregateTerm<T, A> {
  return {
    kind: "aggregate",
    seed,
    accumulator,
    apply: (source) => {
      let result = seed;
      for (const item of source) result = accumulator(result, item);
      return result;
    },
  };
}

/** The non-null values of `source`, through `selector` when there is one. */
function* valuesOf<T>(
  source: Iterable<T>,
  selector: ((item: T) => unknown) | undefined,
): Iterable<unknown> {
  for (const item of source) {
    const value = selector ? selector(item) : item;

    if (value != null) yield value;
  }
}

/** `value` for a sequence that had one, .NET's exception for one that had none. */
function requireValue<R>(value: R | undefined | null): R {
  if (value === undefined || value === null)
    throw new InvalidOperationException(NO_ELEMENTS_MESSAGE);
  return value;
}

/**
 * The smallest of `values`; `undefined` when there are none. `double.NaN` wins over every other
 * value wherever it stands, as in `Enumerable.Min`; `-0` and `0` keep the first one met.
 */
function smallest(values: Iterable<unknown>): unknown {
  let result: unknown;

  for (const value of values) {
    if (isNaNValue(value)) return value;
    if (result === undefined || compareKeys(value, result) < 0) result = value;
  }

  return result;
}

/**
 * The largest of `values`; `undefined` when there are none. `double.NaN` is the result only
 * when it is all there is, as in `Enumerable.Max`.
 */
function largest(values: Iterable<unknown>): unknown {
  let result: unknown;

  for (const value of values) {
    if (result === undefined || isNaNValue(result) || compareKeys(value, result) > 0) {
      result = value;
    }
  }

  return result;
}

function isNaNValue(value: unknown): boolean {
  return typeof value === "number" && Number.isNaN(value);
}

/**
 * The element of `source` with the smallest (`direction` -1) or largest (1) key, the first one
 * on a tie, as `MinBy` / `MaxBy` pick it. A `null` key is skipped; the first element stands in
 * when every key is null. `undefined` for an empty sequence.
 */
function extremeBy<T, K>(
  source: Iterable<T>,
  keySelector: (item: T) => K,
  direction: -1 | 1,
): T | undefined {
  let first: T | undefined;
  let hasFirst = false;
  let result: T | undefined;
  let resultKey: K | undefined;
  let hasResult = false;

  for (const item of source) {
    if (!hasFirst) {
      first = item;
      hasFirst = true;
    }

    const key = keySelector(item);

    if (key == null) continue;

    if (!hasResult || compareKeys(key, resultKey as K) * direction > 0) {
      result = item;
      resultKey = key;
      hasResult = true;
    }
  }

  return hasResult ? result : first;
}

/** The sum as a `number` divided by the count of `values`; `null` when there are none. */
function averageOf(values: Iterable<number>): number | null {
  let total = 0;
  let count = 0;

  for (const value of values) {
    total += value;
    count++;
  }

  return count === 0 ? null : total / count;
}

/** .NET's message for `Average`, `Min`, `Max`, `MinBy` and `MaxBy` over an empty sequence. */
const NO_ELEMENTS_MESSAGE = "Sequence contains no elements";

/** .NET's exception for a sequence with no element, or with none that matches its predicate. */
function noElementException(hasPredicate: boolean): InvalidOperationException {
  return new InvalidOperationException(
    hasPredicate ? "Sequence contains no matching element" : "Sequence contains no elements",
  );
}

/** .NET's exception for a sequence with more than one element (that matches its predicate). */
function moreThanOneElementException(hasPredicate: boolean): InvalidOperationException {
  return new InvalidOperationException(
    hasPredicate
      ? "Sequence contains more than one matching element"
      : "Sequence contains more than one element",
  );
}
