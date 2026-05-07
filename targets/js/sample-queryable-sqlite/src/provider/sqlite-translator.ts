/**
 * Translates a chain of LINQ operator descriptors into a single
 * parameterized SQLite statement. The walker mirrors the runtime's
 * `ExprTree` shape (param/capture/literal/member/call/binary/unary/
 * conditional) and emits SQL fragments alongside a positional
 * parameter array.
 *
 * The translator is intentionally pure — it does no I/O. Repository
 * implementations build a `TranslatedQuery` here and execute it via
 * `bun:sqlite` separately, so the same translator can later be
 * reused over other backends (Postgres, in-memory, etc.) by
 * tweaking the operator → SQL mapping.
 *
 * Scope (MVP): `where` / `select` (single-member projection only) /
 * `orderBy[Descending]` / `thenBy[Descending]` / `take` / `skip`,
 * plus the `count` / `first` / `firstOrDefault` terminals. Anything
 * outside the supported subset throws `UntranslatableTreeError`,
 * which the repository catches and degrades to a closure-path
 * fallback.
 */
import type {
  AnyOperator,
  AnyTerminal,
  ExprBinary,
  ExprConditional,
  ExprMember,
  ExprTree,
  ExprUnary,
  OrderByDescendingOp,
  OrderByOp,
  QueryableMeta,
  SelectOp,
  SkipOp,
  TakeOp,
  ThenByDescendingOp,
  ThenByOp,
  WhereOp,
} from "metano-runtime";

export type Stage = AnyOperator | AnyTerminal;

export interface TranslatedQuery {
  sql: string;
  params: readonly unknown[];
  /**
   * `true` when the chain ends with a projection stage and the
   * caller should read the single column directly off the row
   * instead of routing the row through the entity mapper.
   */
  projected: boolean;
  /**
   * Discriminator for the row-level shape the repository should
   * produce after the query runs (full rows, scalar count, single
   * row, …). Drives the post-execution materialization.
   */
  resultShape: "rows" | "first" | "firstOrDefault" | "count";
}

export class UntranslatableTreeError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "UntranslatableTreeError";
  }
}

interface BuildState {
  selectColumns: string[];
  whereClauses: string[];
  orderBy: { column: string; direction: "ASC" | "DESC" }[];
  limit: number | null;
  offset: number | null;
  params: unknown[];
  paramName: string;
  toColumn: (memberName: string) => string;
}

export function translateChain(
  table: string,
  toColumn: (memberName: string) => string,
  stages: readonly Stage[],
): TranslatedQuery {
  const state = freshState(toColumn);
  const terminal = stages.find(isTerminal) as AnyTerminal | undefined;
  for (const stage of stages) {
    if (isTerminal(stage)) continue;
    applyOperator(stage as AnyOperator, state);
  }
  return renderQuery(table, state, terminal);
}

function freshState(toColumn: (memberName: string) => string): BuildState {
  return {
    selectColumns: [],
    whereClauses: [],
    orderBy: [],
    limit: null,
    offset: null,
    params: [],
    paramName: "",
    toColumn,
  };
}

function isTerminal(stage: Stage): stage is AnyTerminal {
  switch (stage.kind) {
    case "toArray":
    case "toMap":
    case "toSet":
    case "first":
    case "firstOrDefault":
    case "last":
    case "lastOrDefault":
    case "single":
    case "singleOrDefault":
    case "any":
    case "all":
    case "count":
    case "sum":
    case "min":
    case "max":
    case "minBy":
    case "maxBy":
    case "average":
    case "contains":
    case "aggregate":
      return true;
    default:
      return false;
  }
}

function applyOperator(op: AnyOperator, state: BuildState): void {
  switch (op.kind) {
    case "where":
      applyWhere(op as WhereOp<unknown>, state);
      return;
    case "select":
      applySelect(op as SelectOp<unknown, unknown>, state);
      return;
    case "orderBy":
      pushOrder(op.kind, (op as OrderByOp<unknown, unknown>).queryable, state, "ASC", true);
      return;
    case "orderByDescending":
      pushOrder(
        op.kind,
        (op as OrderByDescendingOp<unknown, unknown>).queryable,
        state,
        "DESC",
        true,
      );
      return;
    case "thenBy":
      pushOrder(op.kind, (op as ThenByOp<unknown, unknown>).queryable, state, "ASC", false);
      return;
    case "thenByDescending":
      pushOrder(
        op.kind,
        (op as ThenByDescendingOp<unknown, unknown>).queryable,
        state,
        "DESC",
        false,
      );
      return;
    case "take":
      state.limit = (op as TakeOp<unknown>).count;
      return;
    case "skip":
      state.offset = (op as SkipOp<unknown>).count;
      return;
    default:
      throw new UntranslatableTreeError(`translator: operator '${op.kind}' not supported`);
  }
}

function applyWhere(op: WhereOp<unknown>, state: BuildState): void {
  const meta = requireMeta(op.queryable, "where");
  state.paramName = readParamName(meta.tree) ?? "x";
  state.whereClauses.push(translate(meta.tree, meta.captures ?? {}, state));
}

function applySelect(op: SelectOp<unknown, unknown>, state: BuildState): void {
  const meta = requireMeta(op.queryable, "select");
  state.paramName = readParamName(meta.tree) ?? "x";
  if (meta.tree.kind !== "member") {
    throw new UntranslatableTreeError("translator: select projection must be a member access");
  }
  // Alias the projected column to a stable name so the repository
  // reads the value off the row by key instead of relying on
  // Object.values insertion order.
  const column = translateMember(meta.tree, meta.captures ?? {}, state);
  state.selectColumns = [`${column} AS ${PROJECTION_ALIAS}`];
}

/**
 * Stable alias every projection emits as. The repository looks the
 * value up by name on the materialized row so the projection path
 * does not depend on object key order.
 */
export const PROJECTION_ALIAS = "value";

function pushOrder(
  kind: string,
  meta: QueryableMeta | undefined,
  state: BuildState,
  direction: "ASC" | "DESC",
  resetExisting: boolean,
): void {
  const m = requireMeta(meta, kind);
  state.paramName = readParamName(m.tree) ?? "x";
  if (m.tree.kind !== "member") {
    throw new UntranslatableTreeError(`translator: '${kind}' key must be a member access`);
  }
  const column = translateMember(m.tree, m.captures ?? {}, state);
  if (resetExisting) state.orderBy = [];
  state.orderBy.push({ column, direction });
}

function requireMeta(meta: QueryableMeta | undefined, kind: string): QueryableMeta {
  if (!meta) {
    throw new UntranslatableTreeError(`translator: '${kind}' stage missing queryable meta`);
  }
  return meta;
}

function renderQuery(
  table: string,
  state: BuildState,
  terminal: AnyTerminal | undefined,
): TranslatedQuery {
  const isCount = terminal?.kind === "count";

  // count() with skip/take should only count the windowed rows; the
  // single-statement form can't express that without wrapping in a
  // subquery. Refuse the combination so the repository falls back to
  // the closure path instead of returning a wrong total.
  if (isCount && (state.limit !== null || state.offset !== null)) {
    throw new UntranslatableTreeError(
      "translator: count() combined with take/skip needs a subquery — not supported in this MVP",
    );
  }

  const select = isCount
    ? "COUNT(*) AS c"
    : state.selectColumns.length === 0
      ? "*"
      : state.selectColumns.join(", ");

  let sql = `SELECT ${select} FROM ${table}`;
  if (state.whereClauses.length > 0) sql += ` WHERE ${state.whereClauses.join(" AND ")}`;
  if (state.orderBy.length > 0 && !isCount) {
    sql += ` ORDER BY ${state.orderBy.map((o) => `${o.column} ${o.direction}`).join(", ")}`;
  }

  let limit = state.limit;
  if (
    (terminal?.kind === "first" || terminal?.kind === "firstOrDefault") &&
    (limit === null || limit > 1)
  ) {
    limit = 1;
  }
  if (!isCount) {
    // SQLite rejects OFFSET without LIMIT — emit the well-known
    // "no upper bound" sentinel (-1) when the consumer asked for
    // skip-without-take.
    if (limit !== null) sql += ` LIMIT ${limit}`;
    else if (state.offset !== null) sql += " LIMIT -1";
    if (state.offset !== null) sql += ` OFFSET ${state.offset}`;
  }

  const resultShape: TranslatedQuery["resultShape"] = isCount
    ? "count"
    : terminal?.kind === "first"
      ? "first"
      : terminal?.kind === "firstOrDefault"
        ? "firstOrDefault"
        : "rows";

  return {
    sql,
    params: state.params,
    projected: state.selectColumns.length > 0,
    resultShape,
  };
}

/**
 * Recursive scan for the first lambda parameter node. Visits every
 * child so a predicate that mentions the parameter inside a
 * conditional branch, a method call argument, or any other nested
 * position still resolves correctly.
 */
function readParamName(tree: ExprTree): string | null {
  switch (tree.kind) {
    case "param":
      return tree.name;
    case "capture":
    case "literal":
      return null;
    case "member":
      return readParamName(tree.target);
    case "call":
      return (tree.target ? readParamName(tree.target) : null) ?? firstParamName(tree.args);
    case "binary":
      return readParamName(tree.left) ?? readParamName(tree.right);
    case "unary":
      return readParamName(tree.operand);
    case "conditional":
      return (
        readParamName(tree.condition) ??
        readParamName(tree.whenTrue) ??
        readParamName(tree.whenFalse)
      );
    case "lambda":
    case "new":
      return null;
  }
}

function firstParamName(args: readonly ExprTree[]): string | null {
  for (const arg of args) {
    const found = readParamName(arg);
    if (found !== null) return found;
  }
  return null;
}

function translate(tree: ExprTree, captures: Record<string, unknown>, state: BuildState): string {
  switch (tree.kind) {
    case "param":
      throw new UntranslatableTreeError(
        `translator: bare param '${tree.name}' has no SQL counterpart`,
      );
    case "capture": {
      if (!(tree.name in captures)) {
        throw new UntranslatableTreeError(`translator: capture '${tree.name}' missing from bundle`);
      }
      state.params.push(toBindable(captures[tree.name]));
      return "?";
    }
    case "literal": {
      state.params.push(toBindable(tree.value));
      return "?";
    }
    case "member":
      return translateMember(tree, captures, state);
    case "binary":
      return translateBinary(tree, captures, state);
    case "unary":
      return translateUnary(tree, captures, state);
    case "conditional":
      return translateConditional(tree, captures, state);
    case "call":
    case "lambda":
    case "new":
      throw new UntranslatableTreeError(`translator: ExprTree kind '${tree.kind}' not supported`);
  }
}

function translateMember(
  node: ExprMember,
  _captures: Record<string, unknown>,
  state: BuildState,
): string {
  if (node.target.kind !== "param") {
    throw new UntranslatableTreeError(
      "translator: nested member access not supported (e.g. a.b.c)",
    );
  }
  if (node.target.name !== state.paramName) {
    throw new UntranslatableTreeError(
      `translator: member access targets '${node.target.name}' but the lambda parameter is '${state.paramName}'`,
    );
  }
  return state.toColumn(node.member);
}

function translateBinary(
  node: ExprBinary,
  captures: Record<string, unknown>,
  state: BuildState,
): string {
  const op = SQL_BINARY_OPS[node.op];
  if (op === undefined) {
    throw new UntranslatableTreeError(`translator: binary op '${node.op}' has no SQL counterpart`);
  }
  const left = translate(node.left, captures, state);
  const right = translate(node.right, captures, state);
  return `(${left} ${op} ${right})`;
}

function translateUnary(
  node: ExprUnary,
  captures: Record<string, unknown>,
  state: BuildState,
): string {
  const operand = translate(node.operand, captures, state);
  switch (node.op) {
    case "!":
      return `(NOT ${operand})`;
    case "-":
      return `(-${operand})`;
    case "+":
      return `(+${operand})`;
    default:
      throw new UntranslatableTreeError(`translator: unary op '${node.op}' has no SQL counterpart`);
  }
}

function translateConditional(
  node: ExprConditional,
  captures: Record<string, unknown>,
  state: BuildState,
): string {
  const condition = translate(node.condition, captures, state);
  const whenTrue = translate(node.whenTrue, captures, state);
  const whenFalse = translate(node.whenFalse, captures, state);
  return `(CASE WHEN ${condition} THEN ${whenTrue} ELSE ${whenFalse} END)`;
}

const SQL_BINARY_OPS: Record<string, string> = {
  "==": "=",
  "!=": "<>",
  "<": "<",
  "<=": "<=",
  ">": ">",
  ">=": ">=",
  "&&": "AND",
  "||": "OR",
  "+": "+",
  "-": "-",
  "*": "*",
  "/": "/",
  "%": "%",
};

/**
 * Coerces a JS / wrapped value into something `bun:sqlite` can bind.
 * Numbers / strings / bigints are accepted verbatim; booleans
 * collapse to 0/1 (SQLite has no native boolean); `decimal.js`
 * instances expose a `toNumber()` we use to project to REAL
 * (lossy by design — the sample stores price as REAL).
 */
function toBindable(value: unknown): unknown {
  if (value === null || value === undefined) return null;
  switch (typeof value) {
    case "number":
    case "bigint":
    case "string":
      return value;
    case "boolean":
      return value ? 1 : 0;
    case "object": {
      const decimal = value as { toNumber?: () => number };
      if (typeof decimal.toNumber === "function") return decimal.toNumber();
      throw new UntranslatableTreeError(
        `translator: cannot bind value of type '${value.constructor?.name ?? "object"}'`,
      );
    }
    default:
      throw new UntranslatableTreeError(
        `translator: cannot bind value of typeof '${typeof value}'`,
      );
  }
}
