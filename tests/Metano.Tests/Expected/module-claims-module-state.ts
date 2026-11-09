import { isInt32, isString } from "metano-runtime";

export let fooX: number = 1;
let bazX: number = 3;
export const QuxX: number = 4;

export function fooX$(x: number): number {
  return x + fooX;
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

export function barX(x: number): number {
  return x;
}

export function barS(s: string): number {
  return 0;
}

export function bar(x: number): number;
export function bar(s: string): number;
export function bar(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return barX(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return barS(args[0] as string);
  }

  throw new Error("No matching overload for bar");
}

export function bazX$(x: number): number {
  return x + bazX;
}

export function bazS(s: string): number {
  return 0;
}

export function baz(x: number): number;
export function baz(s: string): number;
export function baz(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return bazX$(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return bazS(args[0] as string);
  }

  throw new Error("No matching overload for baz");
}

export function quxX(x: number): number {
  return x + QuxX;
}

export function quxS(s: string): number {
  return 0;
}

export function qux(x: number): number;
export function qux(s: string): number;
export function qux(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return quxX(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return quxS(args[0] as string);
  }

  throw new Error("No matching overload for qux");
}
