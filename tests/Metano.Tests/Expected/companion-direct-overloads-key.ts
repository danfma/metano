import { isInt32, isInt64, isString } from "metano-runtime";
import type { UId } from "./u-id";

export type Key = string & { readonly __brand: "Key" };

function Key$create(value: string): Key {
  return value as Key;
}

function Key$xL$(l: bigint): string {
  return "x-long";
}

function Key$xU(u: UId): string {
  return "x-uid";
}

function Key$x(l: bigint): string;
function Key$x(u: UId): string;
function Key$x(...args: unknown[]): string {
  if (args.length === 1 && isInt64(args[0])) {
    return Key.xL$(args[0] as bigint);
  }

  if (args.length === 1 && isInt64(args[0])) {
    return Key.xU(args[0] as UId);
  }

  throw new Error("No matching overload for x");
}

function Key$xL(s: string): string {
  return "xl";
}

function Key$pinViaLong(l: bigint): string {
  return "long";
}

function Key$pinU(u: UId): string {
  return "uid";
}

function Key$pin(l: bigint): string;
function Key$pin(u: UId): string;
function Key$pin(...args: unknown[]): string {
  if (args.length === 1 && isInt64(args[0])) {
    return Key.pinViaLong(args[0] as bigint);
  }

  if (args.length === 1 && isInt64(args[0])) {
    return Key.pinU(args[0] as UId);
  }

  throw new Error("No matching overload for pin");
}

function Key$vL(l: bigint): string {
  return "long";
}

/** @internal */
function Key$vU(u: UId): string {
  return "internal";
}

/** @internal */
function Key$vS(s: string): string {
  return "private";
}

function Key$v(l: bigint): string;
/** @internal */
function Key$v(u: UId): string;
/** @internal */
function Key$v(s: string): string;
function Key$v(...args: unknown[]): string {
  if (args.length === 1 && isInt64(args[0])) {
    return Key.vL(args[0] as bigint);
  }

  if (args.length === 1 && isInt64(args[0])) {
    return Key.vU(args[0] as UId);
  }

  if (args.length === 1 && isString(args[0])) {
    return Key.vS(args[0] as string);
  }

  throw new Error("No matching overload for v");
}

function Key$hidden(): string {
  return Key.vS("s");
}

function Key$dLN(l: bigint, n: number = 3): string {
  return "long" + n;
}

function Key$dUN(u: UId, n: number = 5): string {
  return "uid" + n;
}

function Key$d(l: bigint, n?: number): string;
function Key$d(u: UId, n?: number): string;
function Key$d(...args: unknown[]): string {
  if (args.length >= 1 && args.length <= 2 && isInt64(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
    return Key.dLN(args[0] as bigint, args[1] as number);
  }

  if (args.length >= 1 && args.length <= 2 && isInt64(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
    return Key.dUN(args[0] as UId, args[1] as number);
  }

  throw new Error("No matching overload for d");
}

export const Key = {
  create: Key$create,
  xL$: Key$xL$,
  xU: Key$xU,
  x: Key$x,
  xL: Key$xL,
  pinViaLong: Key$pinViaLong,
  pinU: Key$pinU,
  pin: Key$pin,
  vL: Key$vL,
  vU: Key$vU,
  vS: Key$vS,
  v: Key$v,
  hidden: Key$hidden,
  dLN: Key$dLN,
  dUN: Key$dUN,
  d: Key$d,
} as const;
