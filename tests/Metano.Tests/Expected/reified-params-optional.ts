import { UnknownType, isType, type RuntimeType } from "metano-runtime";
import { Circle } from "./circle";

export function tryMatch<T>(o: Object, n: number = 1, T$type: RuntimeType<T> = UnknownType, ...rest: string[]): boolean {
  return isType(o, T$type);
}

export function run(o: Object): boolean {
  const a = tryMatch<Circle>(o, undefined, Circle.$runtimeType);
  const b = tryMatch<Circle>(o, 2, Circle.$runtimeType, "x", "y");

  return a && b;
}
