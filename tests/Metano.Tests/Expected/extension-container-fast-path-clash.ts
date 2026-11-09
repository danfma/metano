import { isInt32, isString } from "metano-runtime";

export function twice(x: number): number {
  return x * 2;
}

export function foldX$(x: number): number {
  return x + 1;
}

export function foldS(s: string): number {
  return s.length + 1;
}

export function fold(x: number): number;
export function fold(s: string): number;
export function fold(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return foldX$(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return foldS(args[0] as string);
  }

  throw new Error("No matching overload for fold");
}
