import { implementsInterface } from "./reflection/implements-interface.ts";
import type { InterfaceType } from "./reflection/runtime-type.ts";

/**
 * C# `System.IDisposable` (spec FR-111): `Dispose()` lowers to `dispose()`, and a C# `using`
 * lowers to `try`/`finally` around the resource. There is no `Symbol.dispose` alias: stable
 * Safari has none, and `using` disposal wraps errors in `SuppressedError`, unlike .NET.
 */
export interface IDisposable {
  dispose(): void;
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
