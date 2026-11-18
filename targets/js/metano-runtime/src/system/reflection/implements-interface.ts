import { hasPlainPrototype, ownRuntimeType } from "./constructor-descriptor.ts";
import type { InterfaceType } from "./runtime-type.ts";

/**
 * C# `value is IFoo`. A class instance is matched through the descriptor of the nearest
 * constructor that declares its own (its `interfaces` are direct plus inherited); a plain
 * object is matched against the interface's `[PlainObject]` implementers. Primitives never
 * implement a user interface.
 */
export function implementsInterface(value: unknown, iface: InterfaceType<unknown>): boolean {
  if (value === null || typeof value !== "object") {
    return false;
  }

  for (
    let current: unknown = (value as { readonly constructor?: unknown }).constructor;
    typeof current === "function";
    current = Object.getPrototypeOf(current)
  ) {
    const descriptor = ownRuntimeType(current);

    if (descriptor?.kind === "class") {
      return listsInterface(descriptor.interfaces, iface);
    }
  }

  return hasPlainPrototype(value) && iface.plainImplementers.some((plain) => plain().test(value));
}

/** Whether `thunks`, or the interfaces they extend (transitively), include `target` by identity. */
export function listsInterface(
  thunks: ReadonlyArray<() => InterfaceType<unknown>>,
  target: InterfaceType<unknown>,
): boolean {
  return thunks.some((thunk) => {
    const candidate = thunk();

    return candidate === target || listsInterface(candidate.baseInterfaces, target);
  });
}
