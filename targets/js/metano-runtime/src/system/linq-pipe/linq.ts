/**
 * Typed `linq()` — left-to-right composition over a source `Iterable<T>`
 * via tagged operator descriptors. Each stage takes the previous stage's
 * output as input. Up to 8 stages typed; beyond that falls back to
 * `unknown`.
 *
 * Two consumption shapes:
 *
 * - All composition operators, no terminal → returns `Iterable<R>`.
 *   Lazy + re-enumerable (each `for…of` rewalks).
 * - Final stage is a terminal → returns the terminal's eager result.
 *
 * The stages are descriptor objects (see `types.ts`). At runtime, `linq`
 * calls `stage.apply(acc)` and feeds the result into the next stage. A
 * future IQueryable provider can intercept the same descriptor list and
 * route to SQL / GraphQL / other query backends without touching the
 * runtime call site.
 */
import type {
  AnyOperator,
  AnyTerminal,
  OperatorBase,
  TerminalBase,
} from "./types.ts";

/* eslint-disable @typescript-eslint/no-explicit-any */
export function linq<T>(source: Iterable<T>): Iterable<T>;
export function linq<T, A>(source: Iterable<T>, op1: OperatorBase<T, A>): Iterable<A>;
export function linq<T, A>(source: Iterable<T>, op1: TerminalBase<T, A>): A;
export function linq<T, A, B>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
): Iterable<B>;
export function linq<T, A, B>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: TerminalBase<A, B>,
): B;
export function linq<T, A, B, C>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
): Iterable<C>;
export function linq<T, A, B, C>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: TerminalBase<B, C>,
): C;
export function linq<T, A, B, C, D>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: OperatorBase<C, D>,
): Iterable<D>;
export function linq<T, A, B, C, D>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: TerminalBase<C, D>,
): D;
export function linq<T, A, B, C, D, E>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: OperatorBase<C, D>,
  op5: OperatorBase<D, E>,
): Iterable<E>;
export function linq<T, A, B, C, D, E>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: OperatorBase<C, D>,
  op5: TerminalBase<D, E>,
): E;
export function linq<T, A, B, C, D, E, F>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: OperatorBase<C, D>,
  op5: OperatorBase<D, E>,
  op6: OperatorBase<E, F>,
): Iterable<F>;
export function linq<T, A, B, C, D, E, F>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: OperatorBase<C, D>,
  op5: OperatorBase<D, E>,
  op6: TerminalBase<E, F>,
): F;
export function linq<T, A, B, C, D, E, F, G>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: OperatorBase<C, D>,
  op5: OperatorBase<D, E>,
  op6: OperatorBase<E, F>,
  op7: OperatorBase<F, G>,
): Iterable<G>;
export function linq<T, A, B, C, D, E, F, G>(
  source: Iterable<T>,
  op1: OperatorBase<T, A>,
  op2: OperatorBase<A, B>,
  op3: OperatorBase<B, C>,
  op4: OperatorBase<C, D>,
  op5: OperatorBase<D, E>,
  op6: OperatorBase<E, F>,
  op7: TerminalBase<F, G>,
): G;
export function linq(
  source: unknown,
  ...ops: (OperatorBase<any, any> | TerminalBase<any, any>)[]
): unknown {
  return ops.reduce((acc, op) => op.apply(acc as Iterable<any>) as any, source as any);
}

/**
 * Helper that exposes the raw descriptor chain to an introspecting
 * consumer (IQueryable provider, query planner, debugger). Returns the
 * stages verbatim so the consumer can switch on `kind` and read the
 * captured lambdas + parameters.
 */
export type Stage = AnyOperator | AnyTerminal;

export function stages(...ops: Stage[]): Stage[] {
  return ops;
}
/* eslint-enable @typescript-eslint/no-explicit-any */
