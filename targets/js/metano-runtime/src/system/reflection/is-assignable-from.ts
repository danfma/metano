import { listsInterface } from "./implements-interface.ts";
import type { ClassType, CollectionType, InterfaceType, RuntimeType } from "./runtime-type.ts";

/**
 * `Type.IsAssignableFrom`: whether a value of `source` can be stored in a `target`. It reads
 * the descriptors' data: class `base` chains, `interfaces` and `baseInterfaces` (transitively),
 * type arguments of a specialization, collection classes/interfaces, and nullable wrappers.
 */
export function isAssignableFrom(target: RuntimeType, source: RuntimeType): boolean {
  if (target === source || target.kind === "unknown" || target.kind === "object") {
    return true;
  }

  if (target.kind === "nullable") {
    return isAssignableFrom(target.inner(), source.kind === "nullable" ? source.inner() : source);
  }

  if (target.kind === "class") {
    return source.kind === "class" && inheritsClass(source, target);
  }

  if (target.kind === "interface") {
    return implementsInterfaceType(source, target);
  }

  if (target.kind === "collection") {
    return source.kind === "collection" && isCollectionAssignable(target, source);
  }

  return false;
}

/**
 * Whether a value of `type` is a C# reference type: variance needs a reference conversion,
 * so `List<int>` is not an `IReadOnlyList<object>` and `List<string>` is. Value types such
 * as `TupleType` (`ValueTuple`) and `ImmutableArray` return `false`.
 */
export function isReferenceType(type: RuntimeType): boolean {
  switch (type.kind) {
    case "primitive":
      return type.primitive === "String";
    case "class":
      return !type.isValueType;
    case "collection":
      return type.collection !== "ImmutableArray";
    case "interface":
    case "plainObject":
    case "array":
    case "delegate":
    case "object":
      return true;
    case "tuple":
    case "opaque":
    case "nullable":
    case "enum":
    case "stringEnum":
    case "unknown":
    case "union":
      return false;
    default: {
      const unhandled: never = type;
      return unhandled;
    }
  }
}

function isCollectionAssignable(
  target: CollectionType<unknown>,
  source: CollectionType<unknown>,
): boolean {
  if (target.ctor !== undefined) {
    if (
      source.ctor === undefined ||
      (source.ctor !== target.ctor && !(source.ctor.prototype instanceof target.ctor))
    ) {
      return false;
    }

    return matchCollectionArguments(target, source.typeArguments, null);
  }

  if (source.collection === target.collection) {
    return matchCollectionArguments(target, source.typeArguments, null);
  }

  const entry = source.interfaces?.find((candidate) => candidate.collection === target.collection);

  if (entry === undefined) {
    return false;
  }

  return entry.arguments === null
    ? target.typeArguments.length === 0
    : matchCollectionArguments(target, source.typeArguments, entry.arguments);
}

function matchCollectionArguments(
  target: CollectionType<unknown>,
  sourceArguments: ReadonlyArray<RuntimeType>,
  mapping: ReadonlyArray<number> | null,
): boolean {
  if (target.typeArguments.length === 0) {
    return true;
  }

  if (sourceArguments.length === 0) {
    return true;
  }

  return target.typeArguments.every((expected, position) => {
    const sourceIndex = mapping === null ? position : (mapping[position] ?? -1);
    const actual = sourceArguments[sourceIndex];

    if (actual === undefined || actual.kind === "unknown" || expected.kind === "unknown") {
      return true;
    }

    return target.variance[position] === "covariant"
      ? actual === expected || (isReferenceType(actual) && isAssignableFrom(expected, actual))
      : actual === expected;
  });
}

function inheritsClass(source: ClassType<unknown>, target: ClassType<unknown>): boolean {
  for (let current: ClassType<unknown> | undefined = source; current; current = current.base?.()) {
    if (current === target || isSameSpecialization(current, target)) {
      return true;
    }
  }

  return false;
}

/** Two specializations of one class with the same type arguments, built separately. */
function isSameSpecialization(a: ClassType<unknown>, b: ClassType<unknown>): boolean {
  return (
    a.typeArguments !== undefined &&
    b.typeArguments !== undefined &&
    a.ctor === b.ctor &&
    a.typeArguments.length === b.typeArguments.length &&
    a.typeArguments.every((argument, index) => argument === b.typeArguments?.[index])
  );
}

function implementsInterfaceType(source: RuntimeType, target: InterfaceType<unknown>): boolean {
  if (source.kind === "interface") {
    return listsInterface([() => source], target);
  }

  for (
    let current: ClassType<unknown> | undefined = source.kind === "class" ? source : undefined;
    current;
    current = current.base?.()
  ) {
    if (listsInterface(current.interfaces, target)) {
      return true;
    }
  }

  return false;
}
