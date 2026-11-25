import type { IEqualityComparer } from "../collections/default-comparer.ts";
import type { Dictionary } from "../collections/dictionary.ts";
import type { HashSet } from "../collections/hash-set.ts";
import type { List } from "../collections/list.ts";
import type { QueryableMeta } from "./expr-tree.ts";
import type { Lookup } from "./lookup.ts";

/**
 * Descriptor-based LINQ runtime — prototype for #20.
 *
 * Every operator factory returns a tagged descriptor object instead of a
 * bare function. Two consumers read the same descriptor:
 *
 * - **Runtime** ({@link linq}) calls `op.apply(source)` to walk lazily.
 * - **IQueryable provider** (future) inspects `op.kind` + the raw lambda
 *   (`predicate` / `selector` / `keySelector` / …) to translate the chain
 *   to SQL, GraphQL, or another query language.
 *
 * Without the discriminator, an operator built from a closure would be
 * opaque — `Function.toString()` is the only handle and it is brittle
 * across minifiers. The tagged-object shape keeps the lambda available
 * verbatim and pairs it with a `kind` literal so providers can switch
 * exhaustively.
 *
 * Bundlers still tree-shake every unused operator: each factory is a
 * standalone function and the union-type definition compiles away.
 */

export type OperatorKind =
  | "where"
  | "select"
  | "selectMany"
  | "take"
  | "skip"
  | "takeWhile"
  | "skipWhile"
  | "distinct"
  | "distinctBy"
  | "concat"
  | "union"
  | "intersect"
  | "except"
  | "append"
  | "prepend"
  | "reverse"
  | "orderBy"
  | "orderByDescending"
  | "thenBy"
  | "thenByDescending"
  | "groupBy"
  | "join"
  | "zip"
  | "defaultIfEmpty";

export type TerminalKind =
  | "toArray"
  | "toMap"
  | "toSet"
  | "toList"
  | "toDictionary"
  | "toHashSet"
  | "toLookup"
  | "sequenceEqual"
  | "first"
  | "firstOrDefault"
  | "last"
  | "lastOrDefault"
  | "single"
  | "singleOrDefault"
  | "any"
  | "all"
  | "count"
  | "sum"
  | "min"
  | "max"
  | "minBy"
  | "maxBy"
  | "average"
  | "contains"
  | "aggregate"
  | "elementAt"
  | "elementAtOrDefault";

/**
 * Common shape every composition operator shares: a discriminator (`kind`)
 * + a runtime walker (`apply`). Concrete descriptors extend this with the
 * operator's typed parameters so providers can read them without casting.
 *
 * Operators that accept a lambda also carry an optional `queryable`
 * field of shape `QueryableMeta` (`{ tree, captures? }`). Populated
 * by the compiler when the call site uses an opt-in
 * IQueryable surface (`[Queryable]` attribute on the C# side, or a
 * BCL `Expression<Func<…>>` parameter type). Absent for plain LINQ-to-
 * Objects calls — providers fall back to running the closure.
 */
export interface OperatorBase<T, R> {
  readonly kind: OperatorKind;
  readonly apply: (source: Iterable<T>) => Iterable<R>;
}

export interface WhereOp<T> extends OperatorBase<T, T> {
  readonly kind: "where";
  readonly predicate: (item: T, index: number) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface SelectOp<T, R> extends OperatorBase<T, R> {
  readonly kind: "select";
  readonly selector: (item: T, index: number) => R;
  readonly queryable?: QueryableMeta;
}

export interface SelectManyOp<T, R> extends OperatorBase<T, R> {
  readonly kind: "selectMany";
  readonly selector: (item: T, index: number) => Iterable<R>;
  readonly queryable?: QueryableMeta;
}

export interface TakeOp<T> extends OperatorBase<T, T> {
  readonly kind: "take";
  readonly count: number;
}

export interface SkipOp<T> extends OperatorBase<T, T> {
  readonly kind: "skip";
  readonly count: number;
}

export interface TakeWhileOp<T> extends OperatorBase<T, T> {
  readonly kind: "takeWhile";
  readonly predicate: (item: T, index: number) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface SkipWhileOp<T> extends OperatorBase<T, T> {
  readonly kind: "skipWhile";
  readonly predicate: (item: T, index: number) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface DistinctOp<T> extends OperatorBase<T, T> {
  readonly kind: "distinct";
  readonly comparer?: IEqualityComparer<T> | null;
}

export interface DistinctByOp<T, K> extends OperatorBase<T, T> {
  readonly kind: "distinctBy";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

export interface ConcatOp<T> extends OperatorBase<T, T> {
  readonly kind: "concat";
  readonly other: Iterable<T>;
}

/** `Union`: the distinct elements of the source, then those of `other` it lacks. */
export interface UnionOp<T> extends OperatorBase<T, T> {
  readonly kind: "union";
  readonly other: Iterable<T>;
}

/** `Intersect`: the distinct elements of the source that `other` holds too. */
export interface IntersectOp<T> extends OperatorBase<T, T> {
  readonly kind: "intersect";
  readonly other: Iterable<T>;
}

/** `Except`: the distinct elements of the source that `other` lacks. */
export interface ExceptOp<T> extends OperatorBase<T, T> {
  readonly kind: "except";
  readonly other: Iterable<T>;
}

export interface AppendOp<T> extends OperatorBase<T, T> {
  readonly kind: "append";
  readonly element: T;
}

export interface PrependOp<T> extends OperatorBase<T, T> {
  readonly kind: "prepend";
  readonly element: T;
}

export interface ReverseOp<T> extends OperatorBase<T, T> {
  readonly kind: "reverse";
}

export interface OrderByOp<T, K> extends OperatorBase<T, T> {
  readonly kind: "orderBy";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

export interface OrderByDescendingOp<T, K> extends OperatorBase<T, T> {
  readonly kind: "orderByDescending";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

export interface ThenByOp<T, K> extends OperatorBase<T, T> {
  readonly kind: "thenBy";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

export interface ThenByDescendingOp<T, K> extends OperatorBase<T, T> {
  readonly kind: "thenByDescending";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

export interface ZipOp<T, U, R> extends OperatorBase<T, R> {
  readonly kind: "zip";
  readonly other: Iterable<U>;
  readonly resultSelector: (first: T, second: U) => R;
  readonly queryable?: QueryableMeta;
}

/**
 * `DefaultIfEmpty`: the source, or `defaultValue` alone when the source is empty. `D` is the
 * miss value: `null` for a reference or nullable element, the element type otherwise.
 */
export interface DefaultIfEmptyOp<T, D extends T | null = T | null> extends OperatorBase<T, T | D> {
  readonly kind: "defaultIfEmpty";
  readonly defaultValue: D;
}

/**
 * Group elements by a key. Each yielded item is an
 * <see cref="Grouping{K, T}"/> — an Iterable that also exposes the
 * shared key. Bundlers can drop the operator if the consumer never
 * imports it.
 */
export interface Grouping<K, T> extends Iterable<T> {
  readonly key: K;
}

export interface GroupByOp<T, K> extends OperatorBase<T, Grouping<K, T>> {
  readonly kind: "groupBy";
  readonly keySelector: (item: T) => K;
  readonly comparer?: IEqualityComparer<K> | null;
  readonly queryable?: QueryableMeta;
}

/** `Join(inner, outerKeySelector, innerKeySelector, resultSelector[, comparer])`. */
export interface JoinOp<T, U, K, R> extends OperatorBase<T, R> {
  readonly kind: "join";
  readonly inner: Iterable<U>;
  readonly outerKeySelector: (item: T) => K;
  readonly innerKeySelector: (item: U) => K;
  readonly resultSelector: (outer: T, inner: U) => R;
  readonly comparer?: IEqualityComparer<K> | null;
}

/** Discriminated union of every composition operator. */
export type AnyOperator =
  | WhereOp<unknown>
  | SelectOp<unknown, unknown>
  | SelectManyOp<unknown, unknown>
  | TakeOp<unknown>
  | SkipOp<unknown>
  | TakeWhileOp<unknown>
  | SkipWhileOp<unknown>
  | DistinctOp<unknown>
  | DistinctByOp<unknown, unknown>
  | ConcatOp<unknown>
  | UnionOp<unknown>
  | IntersectOp<unknown>
  | ExceptOp<unknown>
  | AppendOp<unknown>
  | PrependOp<unknown>
  | ReverseOp<unknown>
  | OrderByOp<unknown, unknown>
  | OrderByDescendingOp<unknown, unknown>
  | ThenByOp<unknown, unknown>
  | ThenByDescendingOp<unknown, unknown>
  | GroupByOp<unknown, unknown>
  | JoinOp<unknown, unknown, unknown, unknown>
  | ZipOp<unknown, unknown, unknown>
  | DefaultIfEmptyOp<unknown>;

/**
 * Common shape every terminal shares. `apply` walks the source and
 * produces a concrete value; providers read `kind` + the captured
 * parameters to decide how to materialize.
 */
export interface TerminalBase<T, R> {
  readonly kind: TerminalKind;
  readonly apply: (source: Iterable<T>) => R;
}

export interface ToArrayTerm<T> extends TerminalBase<T, T[]> {
  readonly kind: "toArray";
}

export interface ToMapTerm<T, K, V> extends TerminalBase<T, Map<K, V>> {
  readonly kind: "toMap";
  readonly keySelector: (item: T) => K;
  readonly valueSelector: (item: T) => V;
  readonly keyQueryable?: QueryableMeta;
  readonly valueQueryable?: QueryableMeta;
}

export interface ToSetTerm<T> extends TerminalBase<T, HashSet<T>> {
  readonly kind: "toSet";
}

export interface ToListTerm<T> extends TerminalBase<T, List<T>> {
  readonly kind: "toList";
}

export interface ToDictionaryTerm<T, K, V> extends TerminalBase<T, Dictionary<K, V>> {
  readonly kind: "toDictionary";
  readonly keySelector: (item: T) => K;
  readonly valueSelector: (item: T) => V;
  readonly keyQueryable?: QueryableMeta;
  readonly valueQueryable?: QueryableMeta;
}

export interface ToHashSetTerm<T> extends TerminalBase<T, HashSet<T>> {
  readonly kind: "toHashSet";
}

export interface FirstTerm<T> extends TerminalBase<T, T> {
  readonly kind: "first";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

/**
 * `D` is the type of the miss value: `null` without a `defaultValue`, `T` with one, or
 * `T | null` for a nullable or optional default.
 */
export interface FirstOrDefaultTerm<T, D extends T | null = T | null>
  extends TerminalBase<T, T | D> {
  readonly kind: "firstOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: D;
  readonly queryable?: QueryableMeta;
}

/** `ElementAt(index)`: throws `ArgumentOutOfRangeException` when `index` is outside the source. */
export interface ElementAtTerm<T> extends TerminalBase<T, T> {
  readonly kind: "elementAt";
  readonly index: number;
}

/** `ElementAtOrDefault(index)`: `defaultValue` when `index` is outside the source. */
export interface ElementAtOrDefaultTerm<T, D extends T | null = T | null>
  extends TerminalBase<T, T | D> {
  readonly kind: "elementAtOrDefault";
  readonly index: number;
  readonly defaultValue: D;
}

export interface LastTerm<T> extends TerminalBase<T, T> {
  readonly kind: "last";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

/**
 * `D` is the type of the miss value: `null` without a `defaultValue`, `T` with one, or
 * `T | null` for a nullable or optional default.
 */
export interface LastOrDefaultTerm<T, D extends T | null = T | null>
  extends TerminalBase<T, T | D> {
  readonly kind: "lastOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: D;
  readonly queryable?: QueryableMeta;
}

export interface SingleTerm<T> extends TerminalBase<T, T> {
  readonly kind: "single";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

/**
 * `D` is the type of the miss value: `null` without a `defaultValue`, `T` with one, or
 * `T | null` for a nullable or optional default.
 */
export interface SingleOrDefaultTerm<T, D extends T | null = T | null>
  extends TerminalBase<T, T | D> {
  readonly kind: "singleOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: D;
  readonly queryable?: QueryableMeta;
}

export interface AnyTerm<T> extends TerminalBase<T, boolean> {
  readonly kind: "any";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface AllTerm<T> extends TerminalBase<T, boolean> {
  readonly kind: "all";
  readonly predicate: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface CountTerm<T> extends TerminalBase<T, number> {
  readonly kind: "count";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

/**
 * `Sum`: `R` is `number`, or the decimal.js value of a `decimal` sum. A `null` the
 * selector (or the source) yields counts as zero, as for `int?`.
 */
export interface SumTerm<T, R = number> extends TerminalBase<T, R> {
  readonly kind: "sum";
  readonly selector?: (item: T) => R | null;
  readonly queryable?: QueryableMeta;
}

/**
 * `Min`: `R` is the type of the values, or `R | null` when an empty sequence
 * or one of nulls gives `null` (a nullable or reference-typed result).
 */
export interface MinTerm<T, R = number> extends TerminalBase<T, R> {
  readonly kind: "min";
  readonly selector?: (item: T) => unknown;
  readonly queryable?: QueryableMeta;
}

/** `Max`: `R` is the type of the values, or `R | null` as for {@link MinTerm}. */
export interface MaxTerm<T, R = number> extends TerminalBase<T, R> {
  readonly kind: "max";
  readonly selector?: (item: T) => unknown;
  readonly queryable?: QueryableMeta;
}

/** `MinBy`: the element with the smallest key; `null` for an empty sequence of a nullable element type. */
export interface MinByTerm<T, K, R = T> extends TerminalBase<T, R> {
  readonly kind: "minBy";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

/** `MaxBy`: the element with the largest key; `null` for an empty sequence of a nullable element type. */
export interface MaxByTerm<T, K, R = T> extends TerminalBase<T, R> {
  readonly kind: "maxBy";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

/** `Average`: `R` is `number`, or the decimal.js value of a `decimal` average; `R | null` for nullable values. */
export interface AverageTerm<T, R = number> extends TerminalBase<T, R> {
  readonly kind: "average";
  readonly selector?: (item: T) => unknown;
  readonly queryable?: QueryableMeta;
}

/**
 * `Sum` over 64-bit values (`long`, `long?`, a `long` selector), which are
 * `bigint`s: a checked sum, nulls skipped, `0n` when empty.
 */
export interface SumInt64Term<T> extends TerminalBase<T, bigint> {
  readonly kind: "sum";
  readonly selector?: (item: T) => bigint | null;
  readonly queryable?: QueryableMeta;
}

/** `Average` over 64-bit values: `R` is `number`, or `number | null` for `long?` values. */
export interface AverageInt64Term<T, R extends number | null> extends TerminalBase<T, R> {
  readonly kind: "average";
  readonly selector?: (item: T) => bigint | null;
  readonly queryable?: QueryableMeta;
}

/** `Min` over 64-bit values: `R` is `bigint`, or `bigint | null` for `long?` values. */
export interface MinInt64Term<T, R extends bigint | null> extends TerminalBase<T, R> {
  readonly kind: "min";
  readonly selector?: (item: T) => bigint | null;
  readonly queryable?: QueryableMeta;
}

/** `Max` over 64-bit values: `R` is `bigint`, or `bigint | null` for `long?` values. */
export interface MaxInt64Term<T, R extends bigint | null> extends TerminalBase<T, R> {
  readonly kind: "max";
  readonly selector?: (item: T) => bigint | null;
  readonly queryable?: QueryableMeta;
}

export interface ContainsTerm<T> extends TerminalBase<T, boolean> {
  readonly kind: "contains";
  readonly value: T;
  readonly comparer?: IEqualityComparer<T> | null;
}

export interface SequenceEqualTerm<T> extends TerminalBase<T, boolean> {
  readonly kind: "sequenceEqual";
  readonly second: Iterable<T>;
  readonly comparer?: IEqualityComparer<T> | null;
}

export interface ToLookupTerm<T, K, V> extends TerminalBase<T, Lookup<K, V>> {
  readonly kind: "toLookup";
  readonly keySelector: (item: T) => K;
  readonly elementSelector?: (item: T) => V;
  readonly comparer?: IEqualityComparer<K> | null;
}

export interface AggregateTerm<T, A> extends TerminalBase<T, A> {
  readonly kind: "aggregate";
  readonly seed: A;
  readonly accumulator: (acc: A, item: T) => A;
}

/** Discriminated union of every terminal. */
export type AnyTerminal =
  | ToArrayTerm<unknown>
  | ToMapTerm<unknown, unknown, unknown>
  | ToSetTerm<unknown>
  | ToListTerm<unknown>
  | ToDictionaryTerm<unknown, unknown, unknown>
  | ToHashSetTerm<unknown>
  | FirstTerm<unknown>
  | FirstOrDefaultTerm<unknown>
  | ElementAtTerm<unknown>
  | ElementAtOrDefaultTerm<unknown>
  | LastTerm<unknown>
  | LastOrDefaultTerm<unknown>
  | SingleTerm<unknown>
  | SingleOrDefaultTerm<unknown>
  | AnyTerm<unknown>
  | AllTerm<unknown>
  | CountTerm<unknown>
  | SumTerm<unknown, unknown>
  | MinTerm<unknown, unknown>
  | MaxTerm<unknown, unknown>
  | MinByTerm<unknown, unknown, unknown>
  | MaxByTerm<unknown, unknown, unknown>
  | AverageTerm<unknown, unknown>
  | SumInt64Term<unknown>
  | AverageInt64Term<unknown, number | null>
  | MinInt64Term<unknown, bigint | null>
  | MaxInt64Term<unknown, bigint | null>
  | ContainsTerm<unknown>
  | SequenceEqualTerm<unknown>
  | ToLookupTerm<unknown, unknown, unknown>
  | AggregateTerm<unknown, unknown>;
