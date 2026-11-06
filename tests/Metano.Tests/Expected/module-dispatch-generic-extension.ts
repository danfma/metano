export function firstFallback<T>(items: T[], fallback: T): T {
  return fallback;
}

export function firstDefault<T>(items: T[]): T {
  return items[0];
}

export function first<T>(items: T[], fallback: T): T;
export function first<T>(items: T[]): T;
export function first(...args: unknown[]): any {
  if (args.length === 2 && Array.isArray(args[0])) {
    return firstFallback(args[0] as any, args[1] as any);
  }

  if (args.length === 1 && Array.isArray(args[0])) {
    return firstDefault(args[0] as any);
  }

  throw new Error("No matching overload for first");
}

import { firstDefault, firstFallback } from "./seqs";

export class Caller {
  constructor() { }

  static alone(): number {
    return firstDefault<number>([1, 2]);
  }

  static withFallback(items: number[]): number {
    return firstFallback<number>(items, 9);
  }
}

export { Caller } from "./caller";
export { first, firstDefault, firstFallback } from "./seqs";
