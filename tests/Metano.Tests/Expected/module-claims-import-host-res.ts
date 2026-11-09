import { isInt32, isString } from "metano-runtime";

export function fooX$(x: number): number {
  return x;
}

export function fooS(s: string): number {
  return 0;
}

export function foo(x: number): number;
export function foo(s: string): number;
export function foo(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return fooX$(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return fooS(args[0] as string);
  }

  throw new Error("No matching overload for foo");
}
