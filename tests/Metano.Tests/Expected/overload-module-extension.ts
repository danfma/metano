import { isInt32, isString } from "metano-runtime";

export function twiceX(x: number): number {
  return x * 2;
}

export function twiceS(s: string): number {
  return s.length * 2;
}

export function twice(x: number): number;
export function twice(s: string): number;
export function twice(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return twiceX(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return twiceS(args[0] as string);
  }

  throw new Error("No matching overload for twice");
}

export function thriceX(x: number): number {
  return x * 3;
}

export function thriceS(s: string): number {
  return s.length * 3;
}

export function thrice(x: number): number;
export function thrice(s: string): number;
export function thrice(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return thriceX(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return thriceS(args[0] as string);
  }

  throw new Error("No matching overload for thrice");
}

import { thriceS, thriceX, twiceS, twiceX } from "./greets";

export class Caller {
  constructor() { }

  static direct(): number {
    return twiceX(21);
  }

  static ofString(): number {
    return twiceS("abcd");
  }

  static asExtension(): number {
    return thriceX(7);
  }

  static ofStringAsExtension(): number {
    return thriceS("abcd");
  }
}

export { Caller } from "./caller";
export { thrice, thriceS, thriceX, twice, twiceS, twiceX } from "./greets";
