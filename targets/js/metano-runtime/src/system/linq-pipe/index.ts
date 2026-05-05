/**
 * Pipe-based LINQ runtime — prototype barrel for #20.
 *
 * Tree-shakeable alternative to the legacy `system/linq/` runtime: each
 * operator is a standalone function the bundler drops when unused.
 *
 * Usage:
 *
 * ```ts
 * import { pipe, where, select, toArray } from "metano-runtime/system/linq-pipe";
 *
 * const result = pipe(
 *   items,
 *   where(x => x.active),
 *   select(x => x.name),
 *   toArray(),
 * );
 * ```
 */
export { pipe } from "./pipe.ts";
export type { OperatorFn, TerminalFn } from "./types.ts";

export {
  where,
  select,
  selectMany,
  take,
  skip,
  takeWhile,
  skipWhile,
  distinct,
  distinctBy,
  concat,
  append,
  prepend,
  reverse,
  orderBy,
  orderByDescending,
  zip,
} from "./operators.ts";

export {
  toArray,
  toMap,
  toSet,
  first,
  firstOrDefault,
  last,
  lastOrDefault,
  single,
  any,
  all,
  count,
  sum,
  min,
  max,
  average,
  contains,
  aggregate,
} from "./terminals.ts";
