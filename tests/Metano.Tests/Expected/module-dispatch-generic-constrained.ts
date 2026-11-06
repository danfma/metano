export interface Ranked {
  readonly rank: number;
}

import type { Ranked } from "./ranked";

export function maxABC<T extends Ranked>(a: T, b: T, c: T): T {
  return a;
}

export function maxAB<T extends Ranked>(a: T, b: T): T {
  return a;
}

export function max<T extends Ranked>(a: T, b: T, c: T): T;
export function max<T extends Ranked>(a: T, b: T): T;
export function max(...args: unknown[]): any {
  if (args.length === 3 && typeof args[0] === "object" && typeof args[1] === "object" && typeof args[2] === "object") {
    return maxABC(args[0] as any, args[1] as any, args[2] as any);
  }

  if (args.length === 2 && typeof args[0] === "object" && typeof args[1] === "object") {
    return maxAB(args[0] as any, args[1] as any);
  }

  throw new Error("No matching overload for max");
}

import type { Ranked } from "./ranked";
import { maxAB, maxABC } from "./ranks";

export class Caller {
  constructor() { }

  top(a: Ranked, b: Ranked): Ranked {
    return maxAB<Ranked>(a, b);
  }

  topThree(a: Ranked, b: Ranked, c: Ranked): Ranked {
    return maxABC<Ranked>(a, b, c);
  }
}

export { Caller } from "./caller";
export type { Ranked } from "./ranked";
export { max, maxAB, maxABC } from "./ranks";
