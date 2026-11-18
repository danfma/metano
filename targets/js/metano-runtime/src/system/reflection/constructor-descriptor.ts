import type { RuntimeType } from "./runtime-type.ts";

/**
 * The descriptor a constructor declares itself: its OWN `$runtimeType` property. A static
 * getter is inherited, so a derived class without one would answer its base's descriptor;
 * `Object.hasOwn` keeps that from being mistaken for the derived class's own (spec 011 FR-018).
 */
export function ownRuntimeType(candidate: unknown): RuntimeType | undefined {
  if (typeof candidate !== "function" || !Object.hasOwn(candidate, "$runtimeType")) {
    return undefined;
  }

  return (candidate as unknown as { readonly $runtimeType: RuntimeType }).$runtimeType;
}

/** The own descriptor of an object value's constructor, when there is one. */
export function ownRuntimeTypeOf(value: unknown): RuntimeType | undefined {
  return value !== null && typeof value === "object"
    ? ownRuntimeType((value as { readonly constructor?: unknown }).constructor)
    : undefined;
}

/** An object literal, `new Object()` or `Object.create(null)`: the shape a `[PlainObject]` has. */
export function hasPlainPrototype(value: object): boolean {
  const prototype = Object.getPrototypeOf(value);

  return prototype === Object.prototype || prototype === null;
}
