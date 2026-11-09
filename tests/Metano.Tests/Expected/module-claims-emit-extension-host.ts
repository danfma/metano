import { isInt32, isString } from "metano-runtime";

export function loneInt32(x: number): number {
  return x;
}

export function loneString(x: string): number {
  return 0;
}

export function lone(x: number): number;
export function lone(x: string): number;
export function lone(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return loneInt32(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return loneString(args[0] as string);
  }

  throw new Error("No matching overload for lone");
}
