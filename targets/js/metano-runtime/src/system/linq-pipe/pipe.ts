/**
 * Typed `pipe()` — RxJS-style left-to-right composition. Each stage takes
 * the previous stage's output as input. Up to 8 stages typed; beyond that
 * falls back to `unknown`.
 */
import type { OperatorFn, TerminalFn } from "./types.ts";

/* eslint-disable @typescript-eslint/no-explicit-any */
export function pipe<T>(source: Iterable<T>): Iterable<T>;
export function pipe<T, A>(source: Iterable<T>, op1: OperatorFn<T, A>): Iterable<A>;
export function pipe<T, A>(source: Iterable<T>, op1: TerminalFn<T, A>): A;
export function pipe<T, A, B>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
): Iterable<B>;
export function pipe<T, A, B>(source: Iterable<T>, op1: OperatorFn<T, A>, op2: TerminalFn<A, B>): B;
export function pipe<T, A, B, C>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
): Iterable<C>;
export function pipe<T, A, B, C>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: TerminalFn<B, C>,
): C;
export function pipe<T, A, B, C, D>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: OperatorFn<C, D>,
): Iterable<D>;
export function pipe<T, A, B, C, D>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: TerminalFn<C, D>,
): D;
export function pipe<T, A, B, C, D, E>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: OperatorFn<C, D>,
  op5: OperatorFn<D, E>,
): Iterable<E>;
export function pipe<T, A, B, C, D, E>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: OperatorFn<C, D>,
  op5: TerminalFn<D, E>,
): E;
export function pipe<T, A, B, C, D, E, F>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: OperatorFn<C, D>,
  op5: OperatorFn<D, E>,
  op6: OperatorFn<E, F>,
): Iterable<F>;
export function pipe<T, A, B, C, D, E, F>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: OperatorFn<C, D>,
  op5: OperatorFn<D, E>,
  op6: TerminalFn<E, F>,
): F;
export function pipe<T, A, B, C, D, E, F, G>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: OperatorFn<C, D>,
  op5: OperatorFn<D, E>,
  op6: OperatorFn<E, F>,
  op7: OperatorFn<F, G>,
): Iterable<G>;
export function pipe<T, A, B, C, D, E, F, G>(
  source: Iterable<T>,
  op1: OperatorFn<T, A>,
  op2: OperatorFn<A, B>,
  op3: OperatorFn<B, C>,
  op4: OperatorFn<C, D>,
  op5: OperatorFn<D, E>,
  op6: OperatorFn<E, F>,
  op7: TerminalFn<F, G>,
): G;
export function pipe(source: unknown, ...ops: ((x: any) => any)[]): unknown {
  return ops.reduce((acc, op) => op(acc), source as any);
}
/* eslint-enable @typescript-eslint/no-explicit-any */
