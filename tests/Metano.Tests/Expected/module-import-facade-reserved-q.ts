import { isInt32, isString } from "metano-runtime";

export function quxX$(x: number): number {
  return x;
}

export function quxS(s: string): number {
  return 0;
}

export function qux(x: number): number;
export function qux(s: string): number;
export function qux(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return quxX$(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return quxS(args[0] as string);
  }

  throw new Error("No matching overload for qux");
}
