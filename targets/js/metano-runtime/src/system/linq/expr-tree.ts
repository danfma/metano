/**
 * Expression tree shape — IR for a lambda body, populated by the
 * compiler when the call site uses an opt-in {@link queryable} surface
 * (see `[Queryable]` attribute on the C# side, or a `Expression<Func<…>>`
 * parameter type lifted from the BCL).
 *
 * Two consumers read the same tree:
 *
 * - **IQueryable provider** (SQL / GraphQL / OData) walks the tree by
 *   `kind` to translate `(u) => u.age > 18` into `WHERE age > 18` —
 *   without parsing closure source.
 * - **Runtime** ignores the tree and calls the paired closure directly
 *   (LINQ-to-Objects fallback).
 *
 * MVP subset: enough for the common LINQ predicate / projector shapes
 * (comparison, member access, method calls, ternary, basic arithmetic).
 * Extend with new variants as concrete cases land.
 */

export type BinaryOp =
  | "=="
  | "!="
  | "<"
  | "<="
  | ">"
  | ">="
  | "+"
  | "-"
  | "*"
  | "/"
  | "%"
  | "&&"
  | "||"
  | "&"
  | "|"
  | "^"
  | "<<"
  | ">>";

export type UnaryOp = "!" | "-" | "+" | "~";

/**
 * Qualified type reference attached to {@link ExprParam}, {@link ExprCapture},
 * and {@link ExprLiteral} nodes. `name` is the simple identifier the provider
 * uses for dispatch / column resolution; `from` is the cross-package origin
 * (the `[EmitPackage]` id on the C# side) so providers can disambiguate two
 * same-named types coming from different packages. Local and primitive types
 * omit `from`.
 */
export interface ExprTypeRef {
  readonly name: string;
  readonly from?: string;
}

/** Reference to a lambda parameter — `(u) => …` introduces `u`. */
export interface ExprParam {
  readonly kind: "param";
  readonly name: string;
  /** Qualified type emitted by the compiler when known. */
  readonly type?: ExprTypeRef;
}

/**
 * Reference to a closure-captured local — `(u) => u.age > minAge`
 * captures `minAge` from the outer scope. Provider reads `name` and
 * looks the runtime value up via the descriptor's separate `captures`
 * record (avoids burning the value into the tree, lets the same tree
 * re-bind to different capture environments).
 */
export interface ExprCapture {
  readonly kind: "capture";
  readonly name: string;
  /** Qualified type emitted by the compiler when known. */
  readonly type?: ExprTypeRef;
}

/** Constant value: literal, captured const, default. */
export interface ExprLiteral {
  readonly kind: "literal";
  readonly value: unknown;
  /** Qualified type emitted by the compiler when known. */
  readonly type?: ExprTypeRef;
}

/** Property / field access: `target.member`. */
export interface ExprMember {
  readonly kind: "member";
  readonly target: ExprTree;
  readonly member: string;
}

/** Method or extension call: `target.method(args)` or `static(args)`. */
export interface ExprCall {
  readonly kind: "call";
  readonly target: ExprTree | null;
  readonly method: string;
  readonly args: readonly ExprTree[];
}

/** Binary op: `left op right`. */
export interface ExprBinary {
  readonly kind: "binary";
  readonly op: BinaryOp;
  readonly left: ExprTree;
  readonly right: ExprTree;
}

/** Unary op: `op operand`. */
export interface ExprUnary {
  readonly kind: "unary";
  readonly op: UnaryOp;
  readonly operand: ExprTree;
}

/** Ternary: `condition ? whenTrue : whenFalse`. */
export interface ExprConditional {
  readonly kind: "conditional";
  readonly condition: ExprTree;
  readonly whenTrue: ExprTree;
  readonly whenFalse: ExprTree;
}

/** Lambda — for nested expressions like `selectMany((u) => u.orders.where(o => …))`. */
export interface ExprLambda {
  readonly kind: "lambda";
  readonly params: readonly ExprParam[];
  readonly body: ExprTree;
}

/** New-object: `new T { p = v, … }` or `new T(args)`. */
export interface ExprNew {
  readonly kind: "new";
  readonly type: string;
  readonly args: readonly ExprTree[];
  readonly initializers?: readonly { readonly name: string; readonly value: ExprTree }[];
}

/** Discriminated union of every supported node. */
export type ExprTree =
  | ExprParam
  | ExprCapture
  | ExprLiteral
  | ExprMember
  | ExprCall
  | ExprBinary
  | ExprUnary
  | ExprConditional
  | ExprLambda
  | ExprNew;

/**
 * Bundle threaded by the compiler when an opt-in queryable surface is
 * detected (`IQueryable<T>` receiver or `[Queryable]` attribute).
 * Carries the lambda's expression tree plus any closure captures the
 * provider needs to evaluate at translation time.
 */
export interface QueryableMeta {
  readonly tree: ExprTree;
  readonly captures?: Readonly<Record<string, unknown>>;
}
