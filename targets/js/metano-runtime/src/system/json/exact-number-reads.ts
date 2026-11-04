import type { JsonTypeInfo, TypeDescriptor, TypeSpec } from "./types.ts";

/**
 * Which reads need a number's exact digits (#457). `deserializeText` reads a
 * spec through the exact tree only when its closure holds such a
 * descriptor, so every other spec keeps plain `JSON.parse`. A `decimal` is
 * the only one today; `long` / `ulong` as `bigint` (#450) join it by
 * answering `true` in `readsNumberText`.
 */

/** Whether the built-in converter of `descriptor` reads a JSON number from its exact text. */
export function readsNumberText(descriptor: TypeDescriptor): boolean {
  return descriptor.kind === "decimal";
}

/**
 * Whether the built-in converter of `descriptor` gets a `JsonNumberText`
 * leaf as is: the readers of exact text, and a nullable, which hands the
 * leaf to its inner converter.
 */
export function forwardsNumberTexts(descriptor: TypeDescriptor): boolean {
  return readsNumberText(descriptor) || descriptor.kind === "nullable";
}

/** The answer of each spec asked about, computed once on its first exact-capable read. */
const needsByInfo = new WeakMap<JsonTypeInfo, boolean>();

/**
 * Whether reading `info` can reach a descriptor that reads exact text:
 * through properties (bases included), derived contracts of a polymorphic
 * base, collection elements, dictionary values, nullables and refs. Thunks
 * (`ref.spec`, `derived.spec`) are called here, outside every generated
 * getter, as the dispatch index calls them.
 */
export function specNeedsExactNumbers(info: JsonTypeInfo): boolean {
  const known = needsByInfo.get(info);

  if (known !== undefined) {
    return known;
  }

  const needs = closureReachesNumberText(info);

  needsByInfo.set(info, needs);

  return needs;
}

/**
 * One walk of a closure. Only the root's answer is kept: a spec inside a
 * cycle is answered before the walk returns to it, so its own answer may be
 * incomplete.
 */
function closureReachesNumberText(info: JsonTypeInfo): boolean {
  const visited = new Set<TypeSpec>();

  const specReaches = (spec: TypeSpec): boolean => {
    if (visited.has(spec)) {
      return false;
    }

    visited.add(spec);

    const reachesThroughProperties = spec.properties.some((property) =>
      descriptorReaches(property.type),
    );

    const reachesThroughBase = () => spec.base !== undefined && specReaches(spec.base);

    const reachesThroughDerived = () =>
      spec.polymorphism?.derived.some((derived) => specReaches(derived.spec())) ?? false;

    return reachesThroughProperties || reachesThroughBase() || reachesThroughDerived();
  };

  const descriptorReaches = (descriptor: TypeDescriptor): boolean => {
    if (readsNumberText(descriptor)) {
      return true;
    }

    switch (descriptor.kind) {
      case "array":
      case "hashSet":
        return descriptorReaches(descriptor.element);
      case "map":
        return descriptorReaches(descriptor.value);
      case "nullable":
        return descriptorReaches(descriptor.inner);
      case "ref":
        return specReaches(descriptor.spec());
      default:
        return false;
    }
  };

  return "descriptor" in info ? descriptorReaches(info.descriptor) : specReaches(info);
}
