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
const readsByInfo = new WeakMap<JsonTypeInfo, boolean>();

/**
 * Whether reading `info` can reach a descriptor that reads exact text:
 * through properties (bases included), derived contracts of a polymorphic
 * base, collection elements, dictionary values, nullables and refs. Thunks
 * (`ref.spec`, `derived.spec`) are called here, outside every generated
 * getter, as the dispatch index calls them.
 */
export function readsNumberTexts(info: JsonTypeInfo): boolean {
  const known = readsByInfo.get(info);

  if (known !== undefined) {
    return known;
  }

  const reads = new ClosureWalk().reachesNumberText(info);

  readsByInfo.set(info, reads);

  return reads;
}

/**
 * One walk of a closure. Only the root's answer is kept: a spec inside a
 * cycle is answered before the walk returns to it, so its own answer may be
 * incomplete.
 */
class ClosureWalk {
  private readonly visited = new Set<TypeSpec>();

  reachesNumberText(info: JsonTypeInfo): boolean {
    return "descriptor" in info ? this.descriptorReaches(info.descriptor) : this.specReaches(info);
  }

  private specReaches(spec: TypeSpec): boolean {
    if (this.visited.has(spec)) {
      return false;
    }

    this.visited.add(spec);

    const reachesThroughProperties = spec.properties.some((property) =>
      this.descriptorReaches(property.type),
    );

    return (
      reachesThroughProperties ||
      (spec.base !== undefined && this.specReaches(spec.base)) ||
      (spec.polymorphism?.derived.some((derived) => this.specReaches(derived.spec())) ?? false)
    );
  }

  private descriptorReaches(descriptor: TypeDescriptor): boolean {
    if (readsNumberText(descriptor)) {
      return true;
    }

    switch (descriptor.kind) {
      case "array":
      case "hashSet":
        return this.descriptorReaches(descriptor.element);
      case "map":
        return this.descriptorReaches(descriptor.value);
      case "nullable":
        return this.descriptorReaches(descriptor.inner);
      case "ref":
        return this.specReaches(descriptor.spec());
      default:
        return false;
    }
  }
}
