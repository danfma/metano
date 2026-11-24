import { List, listType } from "metano-runtime/system/collections";
import { Int32Type, StringType, UnknownType, asType, castTo, createInstance, getRuntimeType, isType, type RuntimeType } from "metano-runtime";
import { first as first$rt, linq } from "metano-runtime/system/linq";
import { Circle } from "./circle.js";
import { Point } from "./point.js";

export function isA<T>(o: Object, T$type: RuntimeType<T>): boolean {
  return isType(o, T$type);
}

export function asA<T>(o: Object, T$type: RuntimeType<T>): T | null {
  return asType(o, T$type);
}

export function castA<T>(o: Object, T$type: RuntimeType<T>): T {
  return castTo<T>(o, T$type);
}

export function typeOf<T>(T$type: RuntimeType<T>): RuntimeType {
  return T$type;
}

export function first<T>(xs: Iterable<T>): T {
  return linq(xs, first$rt());
}

export function make<T>(T$type: RuntimeType<T>): T {
  return createInstance(T$type);
}

export function zero<T>(): T | null {
  return null;
}

export function zeroOf<T>(o: Object, T$type: RuntimeType<T>): T | null {
  return isType(o, T$type) ? o : (T$type.default?.() ?? null) as T;
}

export function runtimeTypeOf<T>(value: T): RuntimeType {
  return getRuntimeType(value!);
}

export function makeList<T>(): List<T> {
  return new List<T>(null, [UnknownType]);
}

export function run(o: Object): void {
  isA<number>(o, Int32Type);
  isA<List<string>>(o, listType(StringType));
  asA<Circle>(o, Circle.$runtimeType);
  castA<Circle>(o, Circle.$runtimeType);
  typeOf<Circle>(Circle.$runtimeType);
  make<Circle>(Circle.$runtimeType);
  zero<number>();
  zeroOf<Point>(o, Point.$runtimeType);
  makeList<Circle>();
}
