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
export { LINQ_STAGES, getStages, linq, stages } from "./linq.ts";
export type { Stage } from "./linq.ts";

export type {
  BinaryOp,
  ExprBinary,
  ExprCall,
  ExprCapture,
  ExprConditional,
  ExprConvert,
  ExprLambda,
  ExprLiteral,
  ExprMember,
  ExprNew,
  ExprParam,
  ExprTree,
  ExprTypeRef,
  ExprUnary,
  QueryableMeta,
  UnaryOp,
} from "./expr-tree.ts";

export {
  ExprTreeVisitor,
  compareValues,
  compileLambdaBody,
  evaluateExprTree,
  readParamName,
} from "./expr-tree-visitor.ts";
export type { ExprTreeScope } from "./expr-tree-visitor.ts";

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
  AverageInt64Term,
  ContainsTerm,
  CountTerm,
  FirstOrDefaultTerm,
  FirstTerm,
  LastOrDefaultTerm,
  LastTerm,
  MaxTerm,
  MaxInt64Term,
  MaxByTerm,
  MinTerm,
  MinInt64Term,
  MinByTerm,
  SingleTerm,
  SingleOrDefaultTerm,
  SumTerm,
  SumInt64Term,
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

export {
  averageInt64,
  averageNullableInt64,
  maxInt64Of,
  maxNullableInt64Of,
  maxUInt64Of,
  minInt64Of,
  minNullableInt64Of,
  minUInt64Of,
  sumInt64,
} from "./int64-terminals.ts";
