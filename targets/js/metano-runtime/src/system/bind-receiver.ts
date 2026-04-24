/**
 * Bridges a C# `[This]`-decorated delegate (whose first parameter
 * is the JavaScript receiver) onto a TypeScript-idiomatic
 * `(this: T, ...args) => R` shape without forcing the generated
 * lambda to use the `function` keyword.
 *
 * The transpiler emits the user's `(self, arg) => ...` lambda as a
 * plain arrow — keeping `this` from the enclosing C# class lexically
 * captured — and wraps it in {@link bindReceiver}. The wrapper is a
 * classic `function` so the dispatching runtime (DOM event loop,
 * native API, etc.) can rebind `this` on invocation; it then
 * forwards that runtime `this` as the first positional argument to
 * the wrapped arrow. The net effect:
 *
 *   - arrow's first parameter (the receiver) carries the runtime
 *     `this` per call,
 *   - any reference to the keyword `this` inside the arrow body
 *     keeps pointing at the outer C# instance via the arrow's
 *     lexical scope — no body rewrite or `const self = this`
 *     shuffle needed.
 *
 * Compatible with delegates whose target-facing type reads
 * `(this: Receiver, ...args: Rest) => R` — the TS compiler infers
 * `Receiver`, `Rest`, and `R` from the wrapped arrow's signature.
 */
export function bindReceiver<Receiver, Rest extends readonly unknown[], R>(
  fn: (receiver: Receiver, ...rest: Rest) => R,
): (this: Receiver, ...args: Rest) => R {
  return function (this: Receiver, ...args: Rest): R {
    return fn(this, ...args);
  };
}
