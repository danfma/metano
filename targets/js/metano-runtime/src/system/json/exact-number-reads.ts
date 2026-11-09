import type { NumberTextLeaves } from "./exact-json-number.ts";
import type { JsonTypeInfo, TypeDescriptor, TypeSpec } from "./types.ts";

/**
 * Which reads need a number's exact text (#457). `deserializeText` reads a
 * spec through the exact tree only when its closure holds such a
 * descriptor, so every other spec keeps plain `JSON.parse`. A `decimal`
 * reads its exact digits; a `long` / `ulong` as `bigint` reads its exact
 * token, digits and shape (spec 012 FR-052, FR-053).
 */

/**
 * Whether the built-in converter of `descriptor` reads an integer token
 * only, so a number with a fraction or an exponent must reach it with its
 * shape (`1.0` is not `1`).
 */
function readsIntegerToken(descriptor: TypeDescriptor): boolean {
  return descriptor.kind === "int64";
}

/** Whether the built-in converter of `descriptor` reads a JSON number from its exact text. */
export function readsNumberText(descriptor: TypeDescriptor): boolean {
  return descriptor.kind === "decimal" || readsIntegerToken(descriptor);
}

/**
 * Whether the built-in converter of `descriptor` gets a `JsonNumberText`
 * leaf as is: the readers of exact text, and a nullable, which hands the
 * leaf to its inner converter.
 */
export function forwardsNumberTexts(descriptor: TypeDescriptor): boolean {
  return readsNumberText(descriptor) || descriptor.kind === "nullable";
}

/** The answer of each spec asked about (`null` for none), computed once on its first text read. */
const leavesByInfo = new WeakMap<JsonTypeInfo, NumberTextLeaves | null>();

/**
 * The numbers the exact tree of `info` must keep as their text, or
 * `undefined` when reading `info` reaches no descriptor that reads exact
 * text: `inexactOrNonInteger` when it reaches a 64-bit integer, `inexact`
 * when it reaches decimals only. The closure runs through properties (bases
 * included), derived contracts of a polymorphic base, collection elements,
 * dictionary values, nullables and refs. Thunks (`ref.spec`,
 * `derived.spec`) are called here, outside every generated getter, as the
 * dispatch index calls them.
 */
export function specNumberTextLeaves(info: JsonTypeInfo): NumberTextLeaves | undefined {
  const known = leavesByInfo.get(info);

  if (known !== undefined) {
    return known ?? undefined;
  }

  const leaves = closureNumberTextLeaves(info);

  leavesByInfo.set(info, leaves ?? null);

  return leaves;
}

function closureNumberTextLeaves(info: JsonTypeInfo): NumberTextLeaves | undefined {
  if (closureReaches(info, readsIntegerToken)) {
    return "inexactOrNonInteger";
  }

  return closureReaches(info, readsNumberText) ? "inexact" : undefined;
}

/**
 * One walk of a closure, looking for a descriptor `matches` accepts. Only
 * the root's answer is kept: a spec inside a cycle is answered before the
 * walk returns to it, so its own answer may be incomplete.
 */
function closureReaches(
  info: JsonTypeInfo,
  matches: (descriptor: TypeDescriptor) => boolean,
): boolean {
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
    if (matches(descriptor)) {
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
