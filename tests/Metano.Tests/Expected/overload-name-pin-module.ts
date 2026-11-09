import { isInt32, isString } from "metano-runtime";

export function twiceX(x: number): number {
  return x * 2;
}

export function twiceText(s: string): number {
  return s.length * 2;
}

export function twice(x: number): number;
export function twice(s: string): number;
export function twice(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return twiceX(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return twiceText(args[0] as string);
  }

  throw new Error("No matching overload for twice");
}

export function thriceX(x: number): number {
  return x * 3;
}

export function thriceOfText(s: string): number {
  return s.length * 3;
}

export function thrice(x: number): number;
export function thrice(s: string): number;
export function thrice(...args: unknown[]): number {
  if (args.length === 1 && isInt32(args[0])) {
    return thriceX(args[0] as number);
  }

  if (args.length === 1 && isString(args[0])) {
    return thriceOfText(args[0] as string);
  }

  throw new Error("No matching overload for thrice");
}

import { thriceOfText, thriceX, twiceText, twiceX } from "./greets";

export class Caller {
  constructor() { }

  static direct(): number {
    return twiceText("abcd") + twiceX(1);
  }

  static asExtension(): number {
    return thriceOfText("abcd") + thriceX(7);
  }
}

export { Caller } from "./caller";
export { thrice, thriceOfText, thriceX, twice, twiceText, twiceX } from "./greets";
