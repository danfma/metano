/**
 * Pipe-based LINQ runtime — prototype for #20.
 *
 * Standalone operator functions composed via {@link pipe}. Bundlers drop
 * operators the user never imports, so a sample using only `where + select +
 * toArray` ships ~3 functions instead of the full {@link EnumerableBase}
 * hierarchy. Lazy semantics preserved via generator-driven `Iterable`.
 *
 * Co-exists with the legacy `system/linq/` runtime — the lowering side picks
 * which form to emit (legacy fluent vs pipe). See ADR-0012 follow-up.
 */

/** A composition operator: takes an `Iterable<T>` and yields `Iterable<R>` lazily. */
export type OperatorFn<T, R> = (source: Iterable<T>) => Iterable<R>;

/** A terminal: takes an `Iterable<T>` and produces a concrete value (eager). */
export type TerminalFn<T, R> = (source: Iterable<T>) => R;
