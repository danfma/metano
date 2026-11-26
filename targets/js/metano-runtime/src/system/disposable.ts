import { implementsInterface } from "./reflection/implements-interface.ts";
import type { InterfaceType } from "./reflection/runtime-type.ts";

/**
 * Runs `dispose` on `resource`: the body of the `[Symbol.dispose]()` alias every generated
 * `IDisposable` implementer declares (spec FR-111), which passes the method that implements
 * `Dispose()` under its emitted name (`disposeResource(this, this.dispose)`).
 *
 * The binding is the polyfill anchor. Building it installs `Symbol.dispose` first when the
 * engine has none (stable Safari), as `Symbol.for("Symbol.dispose")`, the key TypeScript's
 * downlevel `using` helpers then read. The ordering rests on the ESM graph: a generated
 * module imports this one, and this module never imports generated code, so even in an
 * import cycle between generated modules it is evaluated before any class whose
 * `[Symbol.dispose]` key it defines; a bundle that keeps an implementer keeps this
 * initializer (the alias reads it), and one that keeps none keeps nothing (spec 011 FR-043).
 */
export const disposeResource: <T>(resource: T, dispose: (this: T) => void) => void =
  /* @__PURE__ */ createResourceDisposer();

function createResourceDisposer(): <T>(resource: T, dispose: (this: T) => void) => void {
  const symbols = Symbol as { dispose?: typeof Symbol.dispose };

  symbols.dispose ??= Symbol.for("Symbol.dispose") as typeof Symbol.dispose;

  return disposeThrough;
}

function disposeThrough<T>(resource: T, dispose: (this: T) => void): void {
  dispose.call(resource);
}

/**
 * C# `System.IDisposable` (spec FR-111): `Dispose()` lowers to `dispose()`, and a C# `using`
 * lowers to a TypeScript `using` declaration, which calls `[Symbol.dispose]()`. Every
 * generated implementer declares that member as an alias of `dispose()`, so hand-written
 * TypeScript can hold an `IDisposable` in a `using` declaration too.
 */
export interface IDisposable {
  dispose(): void;
  [Symbol.dispose](): void;
}

/**
 * The descriptor of {@link IDisposable}. The test is nominal, as in .NET: a value is
 * `IDisposable` only when its class descriptor lists the interface, never because it has a
 * `dispose` method.
 */
export const IDisposable$runtimeType: InterfaceType<IDisposable> = {
  kind: "interface",
  name: "IDisposable",
  fullName: "System.IDisposable",
  namespace: "System",
  baseInterfaces: [],
  plainImplementers: [],
  test: (value: unknown): value is IDisposable =>
    implementsInterface(value, IDisposable$runtimeType),
};
