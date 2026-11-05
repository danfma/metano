/**
 * The LINQ terminals over C#'s 64-bit values, which are `bigint`s: `Sum`,
 * `Average`, `Min` and `Max` for `long`, `long?` and `ulong` sources or
 * selectors (spec 012 FR-041). They keep the `kind` of the `number`
 * terminals, so an IQueryable provider translates both alike, and .NET's
 * semantics: a checked sum, nulls skipped, `InvalidOperationException` for
 * an empty non-nullable sequence and `null` for an empty nullable one. The
 * `…Of` suffix keeps `minInt64Of` / `maxInt64Of` apart from the scalar
 * `minInt64` / `maxInt64`.
 */
import { InvalidOperationException } from "../exceptions/index.ts";
import { checkedInt64 } from "../int64.ts";
import type { QueryableMeta } from "./expr-tree.ts";
import type { AverageInt64Term, MaxInt64Term, MinInt64Term, SumInt64Term } from "./types.ts";

/** The shape `MinInt64Term` and `MaxInt64Term` share, keyed by their `kind`. */
type ExtremumTerm<T, K extends "min" | "max", R extends bigint | null> = Omit<
  MinInt64Term<T, R>,
  "kind"
> & { readonly kind: K };

/** .NET's message for `Average`, `Min` and `Max` over an empty non-nullable sequence. */
const NO_ELEMENTS_MESSAGE = "Sequence contains no elements";

/** `Sum()` / `Sum(selector)` over `long` or `long?`: checked at every step, nulls skipped, `0n` when empty. */
export function sumInt64<T>(
  selector?: (item: T) => bigint | null,
  queryable?: QueryableMeta,
): SumInt64Term<T> {
  return {
    kind: "sum",
    selector,
    queryable,
    apply: (source) => {
      let total = 0n;

      for (const value of valuesOf(source, selector)) {
        total = checkedInt64(total + value);
      }

      return total;
    },
  };
}

/**
 * `Average()` / `Average(selector)` over `long`: the checked sum as a
 * `number`, divided by the count.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function averageInt64<T>(
  selector?: (item: T) => bigint,
  queryable?: QueryableMeta,
): AverageInt64Term<T, number> {
  return {
    kind: "average",
    selector,
    queryable,
    apply: (source) => requireElement(average(valuesOf(source, selector))),
  };
}

/** `Average()` / `Average(selector)` over `long?`: nulls skipped; `null` when no value is left. */
export function averageNullableInt64<T>(
  selector?: (item: T) => bigint | null,
  queryable?: QueryableMeta,
): AverageInt64Term<T, number | null> {
  return {
    kind: "average",
    selector,
    queryable,
    apply: (source) => average(valuesOf(source, selector)),
  };
}

/**
 * `Min()` / `Min(selector)` over `long`.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function minInt64Of<T>(
  selector?: (item: T) => bigint,
  queryable?: QueryableMeta,
): MinInt64Term<T, bigint> {
  return extremum("min", selector, queryable, isLess, requireElement);
}

/**
 * `Max()` / `Max(selector)` over `long`.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function maxInt64Of<T>(
  selector?: (item: T) => bigint,
  queryable?: QueryableMeta,
): MaxInt64Term<T, bigint> {
  return extremum("max", selector, queryable, isGreater, requireElement);
}

/** `Min()` over `ulong`, whose ordering is the same as `long`'s. */
export function minUInt64Of<T>(
  selector?: (item: T) => bigint,
  queryable?: QueryableMeta,
): MinInt64Term<T, bigint> {
  return minInt64Of(selector, queryable);
}

/** `Max()` over `ulong`, whose ordering is the same as `long`'s. */
export function maxUInt64Of<T>(
  selector?: (item: T) => bigint,
  queryable?: QueryableMeta,
): MaxInt64Term<T, bigint> {
  return maxInt64Of(selector, queryable);
}

/** `Min()` / `Min(selector)` over `long?` or `ulong?`: nulls skipped; `null` when no value is left. */
export function minNullableInt64Of<T>(
  selector?: (item: T) => bigint | null,
  queryable?: QueryableMeta,
): MinInt64Term<T, bigint | null> {
  return extremum("min", selector, queryable, isLess, (value) => value);
}

/** `Max()` / `Max(selector)` over `long?` or `ulong?`: nulls skipped; `null` when no value is left. */
export function maxNullableInt64Of<T>(
  selector?: (item: T) => bigint | null,
  queryable?: QueryableMeta,
): MaxInt64Term<T, bigint | null> {
  return extremum("max", selector, queryable, isGreater, (value) => value);
}

function extremum<T, K extends "min" | "max", R extends bigint | null>(
  kind: K,
  selector: ((item: T) => bigint | null) | undefined,
  queryable: QueryableMeta | undefined,
  replaces: (candidate: bigint, current: bigint) => boolean,
  finish: (value: bigint | null) => R,
): ExtremumTerm<T, K, R> {
  return {
    kind,
    selector,
    queryable,
    apply: (source) => {
      let result: bigint | null = null;

      for (const value of valuesOf(source, selector)) {
        if (result === null || replaces(value, result)) {
          result = value;
        }
      }

      return finish(result);
    },
  };
}

/** The non-null values of `source`, through `selector` when there is one. */
function* valuesOf<T>(
  source: Iterable<T>,
  selector: ((item: T) => bigint | null) | undefined,
): Iterable<bigint> {
  for (const item of source) {
    const value = selector ? selector(item) : (item as unknown as bigint | null);

    if (value != null) {
      yield value;
    }
  }
}

/** The checked sum of `values` as a `number`, divided by their count; `null` when there are none. */
function average(values: Iterable<bigint>): number | null {
  let total = 0n;
  let count = 0;

  for (const value of values) {
    total = checkedInt64(total + value);
    count += 1;
  }

  return count === 0 ? null : Number(total) / count;
}

function requireElement<R>(value: R | null): R {
  if (value === null) {
    throw new InvalidOperationException(NO_ELEMENTS_MESSAGE);
  }

  return value;
}

function isLess(candidate: bigint, current: bigint): boolean {
  return candidate < current;
}

function isGreater(candidate: bigint, current: bigint): boolean {
  return candidate > current;
}
