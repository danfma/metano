/**
 * Descriptor-based LINQ runtime (#20).
 *
 * Each operator is a standalone factory the bundler drops when unused.
 * Every factory returns a tagged descriptor (`kind` + lambdas + `apply`)
 * so runtime walks via `apply` while an IQueryable provider can
 * introspect the chain by reading `kind` and the captured lambdas.
 *
 * Usage:
 *
 * ```ts
 * import { linq, where, select, toArray } from "metano-runtime/system/linq";
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
  ExprCapture,
  ExprConditional,
  ExprLambda,
  ExprLiteral,
  ExprMember,
  ExprNew,
  ExprParam,
  ExprTree,
  ExprUnary,
  QueryableMeta,
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
  GroupByOp,
  Grouping,
  OrderByDescendingOp,
  OrderByOp,
  ThenByDescendingOp,
  ThenByOp,
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
  MaxByTerm,
  MinTerm,
  MinByTerm,
  SingleTerm,
  SingleOrDefaultTerm,
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
  thenBy,
  thenByDescending,
  groupBy,
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
  singleOrDefault,
  any,
  all,
  count,
  sum,
  min,
  max,
  minBy,
  maxBy,
  average,
  contains,
  aggregate,
} from "./terminals.ts";
