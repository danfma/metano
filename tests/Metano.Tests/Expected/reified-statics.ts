import { isType, type RuntimeType } from "metano-runtime";

export class Box<T> {
  constructor() { }

  static is<T>(o: Object, T$type: RuntimeType<T>): boolean {
    return isType(o, T$type);
  }

  static isBoth<T, U>(a: Object, b: Object, T$type: RuntimeType<T>, U$type: RuntimeType<U>): boolean {
    return isType(a, T$type) && isType(b, U$type);
  }

  static typeName<T>(T$type: RuntimeType<T>): string {
    return T$type.name;
  }
}

// ---
import { Int32Type, StringType, formatBoolean } from "metano-runtime";
import { Box } from "./box.js";

export function check(a: Object, b: Object): string {
  return formatBoolean(Box.is(a, Int32Type)) + ":" + formatBoolean(Box.isBoth(a, b, Int32Type, StringType)) + ":" + (Box.typeName(StringType) ?? "");
}
