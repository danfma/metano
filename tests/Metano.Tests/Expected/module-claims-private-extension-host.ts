import { isInt32, isString } from "metano-runtime";

export function hiddenInt32(x: number): number {
  return x;
}

export function hiddenString(x: string): number {
  return 0;
}

export function hidden(x: number): number;
export function hidden(x: string): number;
export function hidden(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return hiddenInt32(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return hiddenString(args[0] as string);
  }

  throw new Error("No matching overload for hidden");
}
