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
  const predicate = compilePredicate(meta);
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
  const selector = compilePredicate(meta);
  return source.map((item) => selector(item));
}

function compilePredicate(meta: QueryableMeta): (item: unknown) => unknown {
  const captures = meta.captures ?? {};
  const paramName = readParamName(meta.tree);
  return (item) => evaluate(meta.tree, { paramName, paramValue: item, captures });
}

/**
 * Walks the tree to find the lambda parameter's name. The MVP shape
 * always exposes a single param node referencing the lambda input;
 * default to "x" when none is found so the provider never throws on a
 * literal-only body.
 */
function readParamName(tree: ExprTree): string {
  if (tree.kind === "param") return tree.name;
  if (tree.kind === "binary") {
    const left = readParamName(tree.left);
    return left === "x" ? readParamName(tree.right) : left;
  }
  if (tree.kind === "unary") return readParamName(tree.operand);
  if (tree.kind === "conditional") return readParamName(tree.condition);
  if (tree.kind === "member") return readParamName(tree.target);
  if (tree.kind === "call" && tree.target) return readParamName(tree.target);
  return "x";
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
  if (target === null || target === undefined) return undefined;
  return (target as Record<string, unknown>)[node.member];
}

function evaluateCall(node: ExprCall, scope: Scope): unknown {
  const target = node.target ? evaluate(node.target, scope) : null;
  const args = node.args.map((a) => evaluate(a, scope));
  if (target === null || target === undefined) {
    throw new Error(`provider: free-function call '${node.method}' has no resolved target`);
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
  if (node.op === "&&") return Boolean(evaluate(node.left, scope)) && Boolean(evaluate(node.right, scope));
  if (node.op === "||") return Boolean(evaluate(node.left, scope)) || Boolean(evaluate(node.right, scope));

  const left = evaluate(node.left, scope) as never;
  const right = evaluate(node.right, scope) as never;
  switch (node.op) {
    case "==":
      // biome-ignore lint/suspicious/noDoubleEquals: predicate parity with C# == comparison
      return left == right;
    case "!=":
      // biome-ignore lint/suspicious/noDoubleEquals: predicate parity with C# != comparison
      return left != right;
    case "<":
      return left < right;
    case "<=":
      return left <= right;
    case ">":
      return left > right;
    case ">=":
      return left >= right;
    case "+":
      return (left as number) + (right as number);
    case "-":
      return (left as number) - (right as number);
    case "*":
      return (left as number) * (right as number);
    case "/":
      return (left as number) / (right as number);
    case "%":
      return (left as number) % (right as number);
    default:
      throw new Error(`provider: unsupported binary op '${node.op}'`);
  }
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
