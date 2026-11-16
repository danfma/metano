/**
 * The LINQ terminals over C#'s `decimal`, which is a decimal.js `Decimal`: `Sum` and `Average`
 * for `decimal` and `decimal?` sources or selectors. They keep the `kind` of the `number`
 * terminals, so an IQueryable provider translates both alike, and .NET's semantics: nulls
 * skipped, `InvalidOperationException` for an empty non-nullable average and `null` for an
 * empty nullable one. The runtime never imports decimal.js (it is the consumer's dependency):
 * the values are read through the structural {@link DecimalValue}, and the sum takes the
 * `Decimal` class to build its `0`. `Min` and `Max` over decimals are the generic `min` and
 * `max`, which order them through {@link compareKeys}.
 */
import { InvalidOperationException } from "../exceptions/invalid-operation-exception.ts";
import type { QueryableMeta } from "./expr-tree.ts";
import type { AverageTerm, SumTerm } from "./types.ts";

/** The decimal.js surface the terminals read. */
export interface DecimalValue<D> {
  plus(other: D): D;
  div(divisor: number): D;
}

/** The decimal.js `Decimal` class, as far as `sumDecimal` builds a zero from it. */
export type DecimalConstructor<D> = new (value: number) => D;

/**
 * `Sum()` / `Sum(selector)` over `decimal` or `decimal?`: nulls skipped, `0` when empty.
 * `zero` is the `Decimal` class.
 */
export function sumDecimal<T, D extends DecimalValue<D>>(
  zero: DecimalConstructor<D>,
  selector?: (item: T) => D | null,
  queryable?: QueryableMeta,
): SumTerm<T, D> {
  return {
    kind: "sum",
    selector,
    queryable,
    apply: (source) => {
      let total = new zero(0);

      for (const value of valuesOf(source, selector)) total = total.plus(value);

      return total;
    },
  };
}

/**
 * `Average()` / `Average(selector)` over `decimal`: the sum divided by the count.
 *
 * @throws InvalidOperationException for an empty sequence.
 */
export function averageDecimal<D extends DecimalValue<D>>(
  selector?: undefined,
  queryable?: QueryableMeta,
): AverageTerm<D, D>;
export function averageDecimal<T, D extends DecimalValue<D>>(
  selector: (item: T) => D,
  queryable?: QueryableMeta,
): AverageTerm<T, D>;
export function averageDecimal<T, D extends DecimalValue<D>>(
  selector?: (item: T) => D,
  queryable?: QueryableMeta,
): AverageTerm<T, D> {
  return {
    kind: "average",
    selector,
    queryable,
    apply: (source) => {
      const average = averageOf(valuesOf(source, selector));

      if (average === null) throw new InvalidOperationException("Sequence contains no elements");

      return average;
    },
  };
}

/** `Average()` / `Average(selector)` over `decimal?`: nulls skipped; `null` when none is left. */
export function averageNullableDecimal<D extends DecimalValue<D>>(
  selector?: undefined,
  queryable?: QueryableMeta,
): AverageTerm<D | null, D | null>;
export function averageNullableDecimal<T, D extends DecimalValue<D>>(
  selector: (item: T) => D | null,
  queryable?: QueryableMeta,
): AverageTerm<T, D | null>;
export function averageNullableDecimal<T, D extends DecimalValue<D>>(
  selector?: (item: T) => D | null,
  queryable?: QueryableMeta,
): AverageTerm<T, D | null> {
  return {
    kind: "average",
    selector,
    queryable,
    apply: (source) => averageOf(valuesOf(source, selector)),
  };
}

/** The non-null values of `source`, through `selector` when there is one. */
function* valuesOf<T, D>(
  source: Iterable<T>,
  selector: ((item: T) => D | null) | undefined,
): Iterable<D> {
  for (const item of source) {
    const value = selector ? selector(item) : (item as unknown as D | null);

    if (value != null) yield value;
  }
}

function averageOf<D extends DecimalValue<D>>(values: Iterable<D>): D | null {
  let total: D | null = null;
  let count = 0;

  for (const value of values) {
    total = total === null ? value : total.plus(value);
    count++;
  }

  return total === null ? null : total.div(count);
}
