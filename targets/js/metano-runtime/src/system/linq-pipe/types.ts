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
  | "any"
  | "all"
  | "count"
  | "sum"
  | "min"
  | "max"
  | "average"
  | "contains"
  | "aggregate";

/**
 * Common shape every composition operator shares: a discriminator (`kind`)
 * + a runtime walker (`apply`). Concrete descriptors extend this with the
 * operator's typed parameters so providers can read them without casting.
 */
export interface OperatorBase<T, R> {
  readonly kind: OperatorKind;
  readonly apply: (source: Iterable<T>) => Iterable<R>;
}

export interface WhereOp<T> extends OperatorBase<T, T> {
  readonly kind: "where";
  readonly predicate: (item: T, index: number) => boolean;
}

export interface SelectOp<T, R> extends OperatorBase<T, R> {
  readonly kind: "select";
  readonly selector: (item: T, index: number) => R;
}

export interface SelectManyOp<T, R> extends OperatorBase<T, R> {
  readonly kind: "selectMany";
  readonly selector: (item: T, index: number) => Iterable<R>;
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
}

export interface SkipWhileOp<T> extends OperatorBase<T, T> {
  readonly kind: "skipWhile";
  readonly predicate: (item: T, index: number) => boolean;
}

export interface DistinctOp<T> extends OperatorBase<T, T> {
  readonly kind: "distinct";
}

export interface DistinctByOp<T, K> extends OperatorBase<T, T> {
  readonly kind: "distinctBy";
  readonly keySelector: (item: T) => K;
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
}

export interface OrderByDescendingOp<T, K> extends OperatorBase<T, T> {
  readonly kind: "orderByDescending";
  readonly keySelector: (item: T) => K;
}

export interface ZipOp<T, U, R> extends OperatorBase<T, R> {
  readonly kind: "zip";
  readonly other: Iterable<U>;
  readonly resultSelector: (first: T, second: U) => R;
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
}

export interface ToSetTerm<T> extends TerminalBase<T, Set<T>> {
  readonly kind: "toSet";
}

export interface FirstTerm<T> extends TerminalBase<T, T> {
  readonly kind: "first";
  readonly predicate?: (item: T) => boolean;
}

export interface FirstOrDefaultTerm<T> extends TerminalBase<T, T | null> {
  readonly kind: "firstOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: T | null;
}

export interface LastTerm<T> extends TerminalBase<T, T> {
  readonly kind: "last";
  readonly predicate?: (item: T) => boolean;
}

export interface LastOrDefaultTerm<T> extends TerminalBase<T, T | null> {
  readonly kind: "lastOrDefault";
  readonly predicate?: (item: T) => boolean;
  readonly defaultValue: T | null;
}

export interface SingleTerm<T> extends TerminalBase<T, T> {
  readonly kind: "single";
  readonly predicate?: (item: T) => boolean;
}

export interface AnyTerm<T> extends TerminalBase<T, boolean> {
  readonly kind: "any";
  readonly predicate?: (item: T) => boolean;
}

export interface AllTerm<T> extends TerminalBase<T, boolean> {
  readonly kind: "all";
  readonly predicate: (item: T) => boolean;
}

export interface CountTerm<T> extends TerminalBase<T, number> {
  readonly kind: "count";
  readonly predicate?: (item: T) => boolean;
}

export interface SumTerm<T> extends TerminalBase<T, number> {
  readonly kind: "sum";
  readonly selector?: (item: T) => number;
}

export interface MinTerm<T> extends TerminalBase<T, number> {
  readonly kind: "min";
  readonly selector?: (item: T) => number;
}

export interface MaxTerm<T> extends TerminalBase<T, number> {
  readonly kind: "max";
  readonly selector?: (item: T) => number;
}

export interface AverageTerm<T> extends TerminalBase<T, number> {
  readonly kind: "average";
  readonly selector?: (item: T) => number;
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
  | AnyTerm<unknown>
  | AllTerm<unknown>
  | CountTerm<unknown>
  | SumTerm<unknown>
  | MinTerm<unknown>
  | MaxTerm<unknown>
  | AverageTerm<unknown>
  | ContainsTerm<unknown>
  | AggregateTerm<unknown, unknown>;
