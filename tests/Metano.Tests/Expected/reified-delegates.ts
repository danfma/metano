import { isType, type RuntimeType } from "metano-runtime";
import { Circle } from "./circle.js";

export function isA<T>(o: Object, T$type: RuntimeType<T>): boolean {
  return isType(o, T$type);
}

export function run(o: Object): boolean {
  const f = (arg: any) => isA(arg, Circle.$runtimeType);

  return f(o);
}
