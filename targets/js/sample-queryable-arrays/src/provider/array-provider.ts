/**
 * Array-backed provider that materializes a `linq()` chain by reading
 * each operator's `queryable` metadata (the IR expression tree) instead
 * of opening the original closure. Each predicate / selector lambda is
 * recompiled into a JS function from its ExprTree shape, then applied
 * over the in-memory source array.
 *
 * Triggered via `runArrayProvider(chain)` where `chain` is the
 * descriptor list the compiler emitted. Stages without queryable meta
 * fall back to the runtime closure (`apply`), so a mixed chain still
 * works — only stages we can introspect get the provider treatment.
 *
 * Scope: predicate evaluation for `where` / `select` over the MVP
 * tree subset (param / capture / literal / member / call / binary /
 * unary / conditional). Real providers extend this same pattern to
 * translate the tree into SQL, GraphQL, or HTTP query strings.
 */
import type {
  AnyOperator,
  ExprBinary,
  ExprCall,
  ExprCapture,
  ExprConditional,
  ExprLiteral,
  ExprMember,
  ExprParam,
  ExprTree,
  ExprUnary,
  QueryableMeta,
  SelectOp,
  WhereOp,
} from "metano-runtime";

/**
 * Result of running the provider over an array source.
 *
 * `materialized` carries the rows after applying every stage that
 * exposed queryable meta (so they bypass their closures). `fellBack`
 * tracks every stage that lacked meta — those still ran but via their
 * native `apply`, so the provider can warn the caller about coverage
 * gaps.
 */
export interface ProviderResult<T> {
  materialized: T[];
  fellBack: AnyOperator["kind"][];
}

type Scope = {
  paramName: string;
  paramValue: unknown;
  captures: Record<string, unknown>;
};

export function runArrayProvider<T>(
  source: Iterable<T>,
  stages: AnyOperator[],
): ProviderResult<unknown> {
  let current: unknown[] = Array.from(source);
  const fellBack: AnyOperator["kind"][] = [];

  for (const stage of stages) {
    if (stage.kind === "where") {
      current = handleWhere(stage as WhereOp<unknown>, current, fellBack);
      continue;
    }
    if (stage.kind === "select") {
      current = handleSelect(stage as SelectOp<unknown, unknown>, current, fellBack);
      continue;
    }

    fellBack.push(stage.kind);
    current = Array.from(stage.apply(current));
  }

  return { materialized: current, fellBack };
}

function handleWhere(
  op: WhereOp<unknown>,
  source: unknown[],
  fellBack: AnyOperator["kind"][],
): unknown[] {
  const meta = op.queryable;
  if (!meta) {
    fellBack.push("where");
    return source.filter((item, index) => op.predicate(item, index));
  }
  const predicate = compileLambdaBody(meta);
  return source.filter((item) => Boolean(predicate(item)));
}

function handleSelect(
  op: SelectOp<unknown, unknown>,
  source: unknown[],
  fellBack: AnyOperator["kind"][],
): unknown[] {
  const meta = op.queryable;
  if (!meta) {
    fellBack.push("select");
    return source.map((item, index) => op.selector(item, index));
  }
  const selector = compileLambdaBody(meta);
  return source.map((item) => selector(item));
}

function compileLambdaBody(meta: QueryableMeta): (item: unknown) => unknown {
  const captures = meta.captures ?? {};
  const paramName = readParamName(meta.tree) ?? "x";
  return (item) => evaluate(meta.tree, { paramName, paramValue: item, captures });
}

/**
 * Recursive scan for the first lambda parameter node. Visits every
 * child of every node kind so a predicate that mentions the parameter
 * inside a conditional branch, a method call argument, or any other
 * nested position still resolves correctly. Returns null when no
 * param node exists — the caller defaults to "x" so a literal-only
 * body still materializes.
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

function evaluate(tree: ExprTree, scope: Scope): unknown {
  switch (tree.kind) {
    case "param":
      return evaluateParam(tree, scope);
    case "capture":
      return evaluateCapture(tree, scope);
    case "literal":
      return evaluateLiteral(tree);
    case "member":
      return evaluateMember(tree, scope);
    case "call":
      return evaluateCall(tree, scope);
    case "binary":
      return evaluateBinary(tree, scope);
    case "unary":
      return evaluateUnary(tree, scope);
    case "conditional":
      return evaluateConditional(tree, scope);
    case "lambda":
    case "new":
      throw new Error(`provider: ExprTree kind '${tree.kind}' not supported in MVP`);
  }
}

function evaluateParam(node: ExprParam, scope: Scope): unknown {
  if (node.name !== scope.paramName) {
    throw new Error(`provider: param '${node.name}' does not match scope '${scope.paramName}'`);
  }
  return scope.paramValue;
}

function evaluateCapture(node: ExprCapture, scope: Scope): unknown {
  if (!(node.name in scope.captures)) {
    throw new Error(`provider: capture '${node.name}' missing from captures bundle`);
  }
  return scope.captures[node.name];
}

function evaluateLiteral(node: ExprLiteral): unknown {
  return node.value;
}

function evaluateMember(node: ExprMember, scope: Scope): unknown {
  const target = evaluate(node.target, scope);
  if (target === null || target === undefined) {
    // Mirror plain JS property-access semantics: reading a member off
    // null/undefined throws a TypeError. The closure path the provider
    // cross-checks against would behave the same way, so divergence
    // here would silently mask bugs.
    throw new TypeError(
      `provider: cannot read member '${node.member}' from ${target === null ? "null" : "undefined"}`,
    );
  }
  return (target as Record<string, unknown>)[node.member];
}

function evaluateCall(node: ExprCall, scope: Scope): unknown {
  // Free-function calls (`target === null`) lack a runtime receiver in
  // the MVP shape — providers that care can resolve them via captures
  // or a global scope override. Surfacing as unsupported here lets the
  // surrounding stage fall back to the closure rather than guess.
  if (node.target === null) {
    throw new Error(
      `provider: free-function call '${node.method}' is not supported in this MVP provider`,
    );
  }
  const target = evaluate(node.target, scope);
  const args = node.args.map((a) => evaluate(a, scope));
  if (target === null || target === undefined) {
    throw new TypeError(
      `provider: cannot invoke '${node.method}' on ${target === null ? "null" : "undefined"}`,
    );
  }
  const fn = (target as Record<string, unknown>)[node.method];
  if (typeof fn !== "function") {
    throw new Error(`provider: '${node.method}' is not callable on the resolved target`);
  }
  return (fn as (...args: unknown[]) => unknown).apply(target, args);
}

function evaluateBinary(node: ExprBinary, scope: Scope): unknown {
  // Short-circuit evaluation for boolean ops keeps semantics aligned
  // with how the C# / TS source would have evaluated the predicate.
  if (node.op === "&&") {
    return Boolean(evaluate(node.left, scope)) && Boolean(evaluate(node.right, scope));
  }
  if (node.op === "||") {
    return Boolean(evaluate(node.left, scope)) || Boolean(evaluate(node.right, scope));
  }

  const left = evaluate(node.left, scope);
  const right = evaluate(node.right, scope);
  switch (node.op) {
    case "==":
      // biome-ignore lint/suspicious/noDoubleEquals: predicate parity with C# == comparison
      return left == right;
    case "!=":
      // biome-ignore lint/suspicious/noDoubleEquals: predicate parity with C# != comparison
      return left != right;
    case "<":
      return compareValues(left, right) < 0;
    case "<=":
      return compareValues(left, right) <= 0;
    case ">":
      return compareValues(left, right) > 0;
    case ">=":
      return compareValues(left, right) >= 0;
    case "+":
      // `+` overloads on string concatenation in both C# and JS. Cast
      // to `number` for the type checker — the JS runtime falls back
      // to string concatenation automatically when either operand is a
      // string, mirroring the closure path.
      return (left as number) + (right as number);
    case "-":
      return (left as number) - (right as number);
    case "*":
      return (left as number) * (right as number);
    case "/":
      // C# `int / int` is integer division; JS `/` is always float.
      // Bridging that requires per-node int-vs-double type info the IR
      // does not currently distinguish (both lower to TS `number`).
      // Until the IR carries the split, the provider mirrors JS
      // semantics — float division — which matches what the closure
      // path emits for the same predicate today.
      return (left as number) / (right as number);
    case "%":
      return (left as number) % (right as number);
    default:
      throw new Error(`provider: unsupported binary op '${node.op}'`);
  }
}

/**
 * Generic ordering comparator: numbers/strings/bigints fall through to
 * the native `<`/`>` operators after a uniform cast, mirroring how
 * the runtime `compareKeys` helper handles primitives. Avoids `as any`
 * while still letting the dynamic evaluator compare heterogeneous
 * primitive types.
 */
function compareValues(left: unknown, right: unknown): number {
  const a = left as number | string | bigint;
  const b = right as number | string | bigint;
  if (a < b) return -1;
  if (a > b) return 1;
  return 0;
}

function evaluateUnary(node: ExprUnary, scope: Scope): unknown {
  const operand = evaluate(node.operand, scope);
  switch (node.op) {
    case "!":
      return !operand;
    case "-":
      return -(operand as number);
    case "+":
      return +(operand as number);
    default:
      throw new Error(`provider: unsupported unary op '${node.op}'`);
  }
}

function evaluateConditional(node: ExprConditional, scope: Scope): unknown {
  return evaluate(node.condition, scope)
    ? evaluate(node.whenTrue, scope)
    : evaluate(node.whenFalse, scope);
}
