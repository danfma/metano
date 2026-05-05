/**
 * Descriptor-based LINQ runtime — prototype barrel for #20.
 *
 * Tree-shakeable alternative to the legacy `system/linq/` runtime: each
 * operator is a standalone factory the bundler drops when unused. Every
 * factory returns a tagged descriptor (`kind` + lambdas + `apply`) so
 * runtime walks via `apply` while a future IQueryable provider can
 * introspect the chain by reading `kind` and the captured lambdas.
 *
 * Usage:
 *
 * ```ts
 * import { linq, where, select, toArray } from "metano-runtime/system/linq-pipe";
 *
 * const result = linq(
 *   items,
 *   where(x => x.active),
 *   select(x => x.name),
 *   toArray(),
 * );
 * ```
 */
export { linq, stages } from "./linq.ts";
export type { Stage } from "./linq.ts";

export type {
  BinaryOp,
  ExprBinary,
  ExprCall,
  ExprConditional,
  ExprLambda,
  ExprLiteral,
  ExprMember,
  ExprNew,
  ExprParam,
  ExprTree,
  ExprUnary,
  UnaryOp,
} from "./expr-tree.ts";

export type {
  AnyOperator,
  AnyTerminal,
  OperatorBase,
  OperatorKind,
  TerminalBase,
  TerminalKind,
  AppendOp,
  ConcatOp,
  DistinctByOp,
  DistinctOp,
  OrderByDescendingOp,
  OrderByOp,
  PrependOp,
  ReverseOp,
  SelectManyOp,
  SelectOp,
  SkipOp,
  SkipWhileOp,
  TakeOp,
  TakeWhileOp,
  WhereOp,
  ZipOp,
  AggregateTerm,
  AllTerm,
  AnyTerm,
  AverageTerm,
  ContainsTerm,
  CountTerm,
  FirstOrDefaultTerm,
  FirstTerm,
  LastOrDefaultTerm,
  LastTerm,
  MaxTerm,
  MinTerm,
  SingleTerm,
  SumTerm,
  ToArrayTerm,
  ToMapTerm,
  ToSetTerm,
} from "./types.ts";

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
