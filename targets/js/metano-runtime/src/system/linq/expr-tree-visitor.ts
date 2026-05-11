/**
 * Generic visitor surface over {@link ExprTree} — the IR shape the
 * compiler emits next to every `[Queryable]` lambda. IQueryable
 * provider authors extend this layer to translate the tree into SQL,
 * GraphQL, OData, HTTP query strings, or any other dialect; they do
 * **not** need to reimplement the recursive walker, parameter scoping,
 * or the primitive comparator.
 *
 * Three entry points cover the common cases:
 *
 * - {@link evaluateExprTree} — pure JS evaluator. Resolves the param,
 *   reads captures, applies operators with the same semantics the
 *   closure path would have used. Mock / in-memory providers can use
 *   it as-is.
 * - {@link compileLambdaBody} — wraps {@link evaluateExprTree} into a
 *   `(item) => unknown` predicate/selector by closing over the
 *   captures bundle and the lambda's first parameter name.
 * - {@link ExprTreeVisitor} — abstract base with one `visitX` per node
 *   kind. The default implementations recurse, so subclasses override
 *   only the nodes they care about (e.g. a SQL emitter overrides
 *   {@link ExprTreeVisitor.visitBinary} to produce operators).
 *
 * The scope helpers ({@link readParamName}, {@link compareValues}) are
 * exported because real providers usually need a parameter alias and a
 * heterogeneous primitive ordering that matches the JS engine.
 */
import type {
  ExprBinary,
  ExprCall,
  ExprCapture,
  ExprConditional,
  ExprLambda,
  ExprLet,
  ExprLiteral,
  ExprMember,
  ExprNew,
  ExprParam,
  ExprRef,
  ExprTree,
  ExprUnary,
  QueryableMeta,
} from "./expr-tree.ts";

/**
 * Lexical scope passed to {@link evaluateExprTree}. Mirrors the way
 * the closure path resolved the lambda body: the param name is bound
 * to the current item, captures are looked up by name in a flat
 * record (the same shape the compiler emits in `QueryableMeta`).
 *
 * `refs` carries the values introduced by enclosing {@link ExprLet}
 * bindings — the evaluator pushes one entry per binding when it
 * descends into a `let` node and looks `ref` nodes up against this
 * record. Callers normally leave it `undefined`; the evaluator
 * materializes it on demand.
 */
export interface ExprTreeScope {
  readonly paramName: string;
  readonly paramValue: unknown;
  readonly captures: Readonly<Record<string, unknown>>;
  readonly refs?: Readonly<Record<string, unknown>>;
}

/** Default fallback name used when a meta tree has no `param` node. */
const DEFAULT_PARAM_NAME = "x";

/**
 * Recursive scan for the first {@link ExprParam} node bound at the
 * *current* lambda level. Visits every child of every node kind so a
 * predicate that mentions the parameter inside a conditional branch,
 * a method-call argument, or any other nested position still
 * resolves. Returns `null` when the body has no param reference —
 * callers default to a fallback (typically `"x"`) so a literal-only
 * body still materializes.
 *
 * Lambda / new boundaries are NOT crossed: a nested lambda binds its
 * own param symbol, and walking through it would shadow the outer
 * binding with the inner one. The outer caller (e.g.
 * {@link compileLambdaBody}) is responsible for resolving the body
 * of a top-level lambda before consulting this helper, if applicable.
 */
export function readParamName(tree: ExprTree): string | null {
  switch (tree.kind) {
    case "param":
      return tree.name;
    case "capture":
    case "literal":
    case "lambda":
    case "new":
    case "ref":
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
    case "let":
      // Bindings are pre-computed subtrees that may contain the param
      // reference too (e.g. `$0 = x.profile.age`); scan them before
      // falling through to the body so a hoisted root still produces
      // the right parameter name.
      for (const binding of tree.bindings) {
        const found = readParamName(binding.value);
        if (found !== null) return found;
      }
      return readParamName(tree.body);
  }
}

function firstParamName(args: readonly ExprTree[]): string | null {
  for (const arg of args) {
    const found = readParamName(arg);
    if (found !== null) return found;
  }
  return null;
}

/**
 * Generic ordering comparator for heterogeneous primitives. Numbers,
 * strings, and bigints fall through to the native `<`/`>` operators
 * after a uniform cast — the same approach the runtime's `compareKeys`
 * helper uses. Lets the dynamic evaluator compare values without
 * burning `as any` casts into every binary branch.
 */
export function compareValues(left: unknown, right: unknown): number {
  const a = left as number | string | bigint;
  const b = right as number | string | bigint;
  if (a < b) return -1;
  if (a > b) return 1;
  return 0;
}

/**
 * Evaluates an {@link ExprTree} against a scope and returns the JS
 * value the tree denotes. Default behavior for mock providers; same
 * semantics the closure path would have produced for the same lambda.
 *
 * Throws when the tree mentions a kind the MVP evaluator does not
 * support (currently `lambda` / `new`) or when a `param` node's name
 * does not match {@link ExprTreeScope.paramName}.
 */
export function evaluateExprTree(tree: ExprTree, scope: ExprTreeScope): unknown {
  switch (tree.kind) {
    case "param":
      return evaluateParam(tree, scope);
    case "capture":
      return evaluateCapture(tree, scope);
    case "literal":
      return tree.value;
    case "member":
      return evaluateMember(tree, scope);
    case "call":
      return evaluateCall(tree, scope);
    case "binary":
      return evaluateBinary(tree, scope);
    case "unary":
      return evaluateUnary(tree, scope);
    case "conditional":
      return evaluateExprTree(tree.condition, scope)
        ? evaluateExprTree(tree.whenTrue, scope)
        : evaluateExprTree(tree.whenFalse, scope);
    case "let":
      return evaluateLet(tree, scope);
    case "ref":
      return evaluateRef(tree, scope);
    case "lambda":
    case "new":
      throw new Error(`evaluateExprTree: kind '${tree.kind}' not supported in MVP`);
  }
}

/**
 * Closes over the captures bundle and returns a `(item) => unknown`
 * function the caller can apply directly — predicate for `where`,
 * projector for `select`, key selector for `orderBy`, etc.
 *
 * The parameter name is read from the first {@link ExprParam} node in
 * `meta.tree`; pass {@link fallbackParamName} (defaults to `"x"`) to
 * support literal-only bodies that never mention the param.
 */
export function compileLambdaBody(
  meta: QueryableMeta,
  fallbackParamName: string = DEFAULT_PARAM_NAME,
): (item: unknown) => unknown {
  const captures = meta.captures ?? {};
  // Top-level lambda meta: bind to the outer lambda's first param so
  // nested lambdas (whose `params` would shadow it) cannot hijack the
  // resolution via the body scan.
  const topLevelParam = meta.tree.kind === "lambda" ? meta.tree.params[0]?.name : null;
  const paramName = topLevelParam ?? readParamName(meta.tree) ?? fallbackParamName;
  return (item) => evaluateExprTree(meta.tree, { paramName, paramValue: item, captures });
}

function evaluateParam(node: ExprParam, scope: ExprTreeScope): unknown {
  if (node.name !== scope.paramName) {
    throw new Error(
      `evaluateExprTree: param '${node.name}' does not match scope '${scope.paramName}'`,
    );
  }
  return scope.paramValue;
}

function evaluateCapture(node: ExprCapture, scope: ExprTreeScope): unknown {
  if (!(node.name in scope.captures)) {
    throw new Error(`evaluateExprTree: capture '${node.name}' missing from captures bundle`);
  }
  return scope.captures[node.name];
}

function evaluateMember(node: ExprMember, scope: ExprTreeScope): unknown {
  const target = evaluateExprTree(node.target, scope);
  if (target === null || target === undefined) {
    // Mirror plain JS property-access semantics: reading a member off
    // null/undefined throws a TypeError. The closure path the provider
    // cross-checks against behaves the same way, so divergence here
    // would silently mask bugs.
    throw new TypeError(
      `evaluateExprTree: cannot read member '${node.member}' from ${
        target === null ? "null" : "undefined"
      }`,
    );
  }
  return (target as Record<string, unknown>)[node.member];
}

function evaluateCall(node: ExprCall, scope: ExprTreeScope): unknown {
  // Free-function calls (`target === null`) lack a runtime receiver in
  // the MVP shape — providers that care can resolve them via captures
  // or a global-scope override. Surfacing as unsupported here lets the
  // surrounding stage fall back to the closure rather than guess.
  if (node.target === null) {
    throw new Error(
      `evaluateExprTree: free-function call '${node.method}' is not supported by the default evaluator`,
    );
  }
  const target = evaluateExprTree(node.target, scope);
  const args = node.args.map((a) => evaluateExprTree(a, scope));
  if (target === null || target === undefined) {
    throw new TypeError(
      `evaluateExprTree: cannot invoke '${node.method}' on ${
        target === null ? "null" : "undefined"
      }`,
    );
  }
  const fn = (target as Record<string, unknown>)[node.method];
  if (typeof fn !== "function") {
    throw new Error(`evaluateExprTree: '${node.method}' is not callable on the resolved target`);
  }
  return (fn as (...args: unknown[]) => unknown).apply(target, args);
}

function evaluateBinary(node: ExprBinary, scope: ExprTreeScope): unknown {
  // Short-circuit boolean ops keep semantics aligned with the closure
  // path; lazy evaluation matters when one side has side effects.
  if (node.op === "&&") {
    return (
      Boolean(evaluateExprTree(node.left, scope)) && Boolean(evaluateExprTree(node.right, scope))
    );
  }
  if (node.op === "||") {
    return (
      Boolean(evaluateExprTree(node.left, scope)) || Boolean(evaluateExprTree(node.right, scope))
    );
  }

  const left = evaluateExprTree(node.left, scope);
  const right = evaluateExprTree(node.right, scope);
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
      // `+` overloads on string concatenation in both C# and JS. The
      // cast satisfies the type checker — the JS runtime falls back to
      // string concatenation automatically when either operand is a
      // string, matching the closure path.
      return (left as number) + (right as number);
    case "-":
      return (left as number) - (right as number);
    case "*":
      return (left as number) * (right as number);
    case "/":
      // C# `int / int` is integer division; JS `/` is always float.
      // Bridging requires per-node int-vs-double type info the IR does
      // not currently distinguish. The default evaluator mirrors JS
      // semantics — float division — which matches what the closure
      // path emits for the same predicate today.
      return (left as number) / (right as number);
    case "%":
      return (left as number) % (right as number);
    default:
      throw new Error(`evaluateExprTree: unsupported binary op '${node.op}'`);
  }
}

function evaluateLet(node: ExprLet, scope: ExprTreeScope): unknown {
  // Evaluate bindings in declaration order so an earlier binding's
  // value is visible to a later binding (matches how the compiler
  // emits the $0/$1/… sequence).
  const refs: Record<string, unknown> = { ...(scope.refs ?? {}) };
  for (const binding of node.bindings) {
    refs[binding.name] = evaluateExprTree(binding.value, { ...scope, refs });
  }
  return evaluateExprTree(node.body, { ...scope, refs });
}

function evaluateRef(node: ExprRef, scope: ExprTreeScope): unknown {
  const refs = scope.refs;
  if (refs === undefined || !(node.name in refs)) {
    throw new Error(
      `evaluateExprTree: ref '${node.name}' resolves outside any enclosing let binding`,
    );
  }
  return refs[node.name];
}

function evaluateUnary(node: ExprUnary, scope: ExprTreeScope): unknown {
  const operand = evaluateExprTree(node.operand, scope);
  switch (node.op) {
    case "!":
      return !operand;
    case "-":
      return -(operand as number);
    case "+":
      return +(operand as number);
    default:
      throw new Error(`evaluateExprTree: unsupported unary op '${node.op}'`);
  }
}

/**
 * Abstract visitor over {@link ExprTree}. Each `visitX` method handles
 * one node kind. Default implementations recurse via {@link visit},
 * so a subclass only overrides the methods it needs.
 *
 * Generic parameter `R` is the value the visitor produces — a SQL
 * fragment, a GraphQL field, a JS value, an AST node in another IR,
 * etc. Implementations decide how children's results combine in their
 * override (e.g. a SQL emitter's `visitBinary` calls `this.visit(left)`
 * and `this.visit(right)` and emits `(left) op (right)`).
 *
 * Example:
 *
 * ```ts
 * class SqlPredicate extends ExprTreeVisitor<string> {
 *   protected visitBinary(node: ExprBinary): string {
 *     return `(${this.visit(node.left)} ${node.op} ${this.visit(node.right)})`;
 *   }
 *   protected visitMember(node: ExprMember): string {
 *     return `${this.visit(node.target)}.${node.member}`;
 *   }
 *   protected visitParam(node: ExprParam): string {
 *     return node.name;
 *   }
 *   protected visitLiteral(node: ExprLiteral): string {
 *     return JSON.stringify(node.value);
 *   }
 *   // capture / call / unary / conditional inherit the default recurse
 *   // behavior; override the ones the dialect actually translates.
 * }
 * ```
 */
export abstract class ExprTreeVisitor<R> {
  /**
   * Dispatches to the appropriate `visitX` method. Subclasses can
   * override to add tracing or to short-circuit on a specific node
   * shape, but the default implementation is usually enough.
   */
  visit(tree: ExprTree): R {
    switch (tree.kind) {
      case "param":
        return this.visitParam(tree);
      case "capture":
        return this.visitCapture(tree);
      case "literal":
        return this.visitLiteral(tree);
      case "member":
        return this.visitMember(tree);
      case "call":
        return this.visitCall(tree);
      case "binary":
        return this.visitBinary(tree);
      case "unary":
        return this.visitUnary(tree);
      case "conditional":
        return this.visitConditional(tree);
      case "lambda":
        return this.visitLambda(tree);
      case "new":
        return this.visitNew(tree);
      case "let":
        return this.visitLet(tree);
      case "ref":
        return this.visitRef(tree);
    }
  }

  /**
   * Combines the results of recursing into multiple children. Default
   * returns the **last** result, which suits visitors that produce a
   * value per node and only need the final aggregate (e.g. a side-effect
   * collector). When the child list is empty (e.g. a free-function
   * call with no args, an empty `new` expression), the visitor falls
   * back to {@link leaf} so subclasses still get one well-defined
   * extension point. Override to fold lists, concatenate strings, etc.
   */
  protected combine(results: readonly R[]): R {
    if (results.length === 0) {
      return this.leaf();
    }
    // Non-null assertion is safe — we just guarded the empty case.
    return results[results.length - 1]!;
  }

  protected visitParam(_node: ExprParam): R {
    return this.leaf();
  }

  protected visitCapture(_node: ExprCapture): R {
    return this.leaf();
  }

  protected visitLiteral(_node: ExprLiteral): R {
    return this.leaf();
  }

  protected visitMember(node: ExprMember): R {
    // Single-child interior node still routes through `combine` so a
    // subclass that overrides only `combine` (e.g. a SQL emitter that
    // parenthesizes every interior node) sees every recurse path
    // consistently — including the unary/member/lambda branches.
    return this.combine([this.visit(node.target)]);
  }

  protected visitCall(node: ExprCall): R {
    const results: R[] = [];
    if (node.target !== null) results.push(this.visit(node.target));
    for (const arg of node.args) results.push(this.visit(arg));
    return this.combine(results);
  }

  protected visitBinary(node: ExprBinary): R {
    return this.combine([this.visit(node.left), this.visit(node.right)]);
  }

  protected visitUnary(node: ExprUnary): R {
    return this.combine([this.visit(node.operand)]);
  }

  protected visitConditional(node: ExprConditional): R {
    return this.combine([
      this.visit(node.condition),
      this.visit(node.whenTrue),
      this.visit(node.whenFalse),
    ]);
  }

  protected visitLambda(node: ExprLambda): R {
    return this.combine([this.visit(node.body)]);
  }

  protected visitNew(node: ExprNew): R {
    const results = node.args.map((a) => this.visit(a));
    for (const init of node.initializers ?? []) {
      results.push(this.visit(init.value));
    }
    return this.combine(results);
  }

  /**
   * Hoisted common-subexpression wrapper produced by the compiler
   * (#209). Default behavior recurses through every binding value
   * (in declaration order) and then the body, threading the children
   * through {@link combine}.
   */
  protected visitLet(node: ExprLet): R {
    const results: R[] = [];
    for (const binding of node.bindings) results.push(this.visit(binding.value));
    results.push(this.visit(node.body));
    return this.combine(results);
  }

  /**
   * Reference to a binding introduced by an enclosing {@link ExprLet}.
   * Default behavior is to treat it as a leaf — subclasses that build
   * a SQL/expression dialect should override and emit the alias the
   * matching binding generated.
   */
  protected visitRef(_node: ExprRef): R {
    return this.leaf();
  }

  /**
   * Default result for true leaf nodes ({@link visitParam},
   * {@link visitCapture}, {@link visitLiteral}) AND for interior
   * nodes whose recurse-list is empty (e.g. a zero-arg method call,
   * a `new` expression with no args and no initializers). Subclasses
   * that inherit the default recurse must override <c>leaf</c> so
   * {@link combine} has something to fold — the base throws to
   * force a deliberate choice.
   */
  protected leaf(): R {
    throw new Error(
      "ExprTreeVisitor.leaf: no default leaf value — override visitParam/visitCapture/visitLiteral or leaf()",
    );
  }
}
