import type { QueryableMeta } from "./expr-tree.ts";

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
  | "append"
  | "prepend"
  | "reverse"
  | "orderBy"
  | "orderByDescending"
  | "thenBy"
  | "thenByDescending"
  | "zip";

export type TerminalKind =
  | "toArray"
  | "toMap"
  | "toSet"
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
  | "aggregate";

/**
 * Common shape every composition operator shares: a discriminator (`kind`)
 * + a runtime walker (`apply`). Concrete descriptors extend this with the
 * operator's typed parameters so providers can read them without casting.
 *
 * Operators that accept a lambda also carry an optional `expression`
 * field. Populated by the compiler when the call site uses an opt-in
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
  | AppendOp<unknown>
  | PrependOp<unknown>
  | ReverseOp<unknown>
  | OrderByOp<unknown, unknown>
  | OrderByDescendingOp<unknown, unknown>
  | ThenByOp<unknown, unknown>
  | ThenByDescendingOp<unknown, unknown>
  | ZipOp<unknown, unknown, unknown>;

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

export interface ToSetTerm<T> extends TerminalBase<T, Set<T>> {
  readonly kind: "toSet";
}

export interface FirstTerm<T> extends TerminalBase<T, T> {
  readonly kind: "first";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface FirstOrDefaultTerm<T> extends TerminalBase<T, T | null> {
  readonly kind: "firstOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: T | null;
  readonly queryable?: QueryableMeta;
}

export interface LastTerm<T> extends TerminalBase<T, T> {
  readonly kind: "last";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface LastOrDefaultTerm<T> extends TerminalBase<T, T | null> {
  readonly kind: "lastOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: T | null;
  readonly queryable?: QueryableMeta;
}

export interface SingleTerm<T> extends TerminalBase<T, T> {
  readonly kind: "single";
  readonly predicate?: (item: T) => boolean;
  readonly queryable?: QueryableMeta;
}

export interface SingleOrDefaultTerm<T> extends TerminalBase<T, T | null> {
  readonly kind: "singleOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: T | null;
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

export interface SumTerm<T> extends TerminalBase<T, number> {
  readonly kind: "sum";
  readonly selector?: (item: T) => number;
  readonly queryable?: QueryableMeta;
}

export interface MinTerm<T> extends TerminalBase<T, number> {
  readonly kind: "min";
  readonly selector?: (item: T) => number;
  readonly queryable?: QueryableMeta;
}

export interface MaxTerm<T> extends TerminalBase<T, number> {
  readonly kind: "max";
  readonly selector?: (item: T) => number;
  readonly queryable?: QueryableMeta;
}

export interface MinByTerm<T, K> extends TerminalBase<T, T> {
  readonly kind: "minBy";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

export interface MaxByTerm<T, K> extends TerminalBase<T, T> {
  readonly kind: "maxBy";
  readonly keySelector: (item: T) => K;
  readonly queryable?: QueryableMeta;
}

export interface AverageTerm<T> extends TerminalBase<T, number> {
  readonly kind: "average";
  readonly selector?: (item: T) => number;
  readonly queryable?: QueryableMeta;
}

export interface ContainsTerm<T> extends TerminalBase<T, boolean> {
  readonly kind: "contains";
  readonly value: T;
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
  | FirstTerm<unknown>
  | FirstOrDefaultTerm<unknown>
  | LastTerm<unknown>
  | LastOrDefaultTerm<unknown>
  | SingleTerm<unknown>
  | SingleOrDefaultTerm<unknown>
  | AnyTerm<unknown>
  | AllTerm<unknown>
  | CountTerm<unknown>
  | SumTerm<unknown>
  | MinTerm<unknown>
  | MaxTerm<unknown>
  | MinByTerm<unknown, unknown>
  | MaxByTerm<unknown, unknown>
  | AverageTerm<unknown>
  | ContainsTerm<unknown>
  | AggregateTerm<unknown, unknown>;
