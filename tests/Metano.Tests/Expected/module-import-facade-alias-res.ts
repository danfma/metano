import { isInt32, isString } from "metano-runtime";

export function otherX$(x: number): number {
  return x;
}

export function otherS(s: string): number {
  return 0;
}

export function other(x: number): number;
export function other(s: string): number;
export function other(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return otherX$(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return otherS(args[0] as string);
  }

  throw new Error("No matching overload for other");
}

export function fooY$(y: number): number {
  return y;
}

export function fooS(s: string): number {
  return 0;
}

export function foo(y: number): number;
export function foo(s: string): number;
export function foo(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return fooY$(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return fooS(args[0] as string);
  }

  throw new Error("No matching overload for foo");
}
