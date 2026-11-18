import { listsInterface } from "./implements-interface.ts";
import type { ClassType, InterfaceType, RuntimeType } from "./runtime-type.ts";

/**
 * `Type.IsAssignableFrom`: whether a value of `source` can be stored in a `target`. It reads
 * the descriptors' data: class `base` chains, `interfaces` and `baseInterfaces` (transitively),
 * type arguments of a specialization, and nullable wrappers.
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

  return false;
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
