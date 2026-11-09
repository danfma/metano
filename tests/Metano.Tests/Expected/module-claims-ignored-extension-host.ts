import { isInt32, isString } from "metano-runtime";

export function plainInt32(x: number): number {
  return x;
}

export function plainString(x: string): number {
  return 0;
}

export function plain(x: number): number;
export function plain(x: string): number;
export function plain(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return plainInt32(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return plainString(args[0] as string);
  }

  throw new Error("No matching overload for plain");
}
