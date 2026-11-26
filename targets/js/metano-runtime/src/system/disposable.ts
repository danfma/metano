import { implementsInterface } from "./reflection/implements-interface.ts";
import type { InterfaceType } from "./reflection/runtime-type.ts";

/**
 * Disposes `resource`: the body of the `[Symbol.dispose]()` alias every generated
 * `IDisposable` implementer declares (spec FR-111). Building it installs `Symbol.dispose`
 * first when the engine has none (stable Safari), as `Symbol.for("Symbol.dispose")`, the key
 * TypeScript's downlevel `using` helpers then read. The alias reads this binding, so a bundle
 * that keeps a generated implementer keeps the initializer too, ahead of the class whose
 * `[Symbol.dispose]` key it defines; a bundle that keeps no implementer keeps nothing
 * (spec 011 FR-043).
 */
export const disposeResource: (resource: IDisposable) => void =
  /* @__PURE__ */ createResourceDisposer();

function createResourceDisposer(): (resource: IDisposable) => void {
  const symbols = Symbol as { dispose?: typeof Symbol.dispose };

  symbols.dispose ??= Symbol.for("Symbol.dispose") as typeof Symbol.dispose;

  return disposeThrough;
}

function disposeThrough(resource: IDisposable): void {
  resource.dispose();
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
