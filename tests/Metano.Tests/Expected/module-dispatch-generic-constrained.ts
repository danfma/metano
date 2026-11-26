import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface Ranked {
  readonly rank: number;
}

export const Ranked$runtimeType: InterfaceType<Ranked> = {
  kind: "interface",
  name: "Ranked",
  fullName: "Ranked",
  namespace: "",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is Ranked => implementsInterface(v, Ranked$runtimeType),
};

import { isType } from "metano-runtime";
import { Ranked$runtimeType, type Ranked } from "./ranked.js";

export function maxABC<T extends Ranked>(a: T, b: T, c: T): T {
  return a;
}

export function maxAB<T extends Ranked>(a: T, b: T): T {
  return a;
}

export function max<T extends Ranked>(a: T, b: T, c: T): T;
export function max<T extends Ranked>(a: T, b: T): T;
export function max(...args: unknown[]): any {
  if (args.length === 3 && isType(args[0], Ranked$runtimeType) && isType(args[1], Ranked$runtimeType) && isType(args[2], Ranked$runtimeType)) {
    return maxABC(args[0] as any, args[1] as any, args[2] as any);
  }

  if (args.length === 2 && isType(args[0], Ranked$runtimeType) && isType(args[1], Ranked$runtimeType)) {
    return maxAB(args[0] as any, args[1] as any);
  }

  throw new Error("No matching overload for max");
}

import type { Ranked } from "./ranked.js";
import { maxAB, maxABC } from "./ranks.js";

export class Caller {
  top(a: Ranked, b: Ranked): Ranked {
    return maxAB<Ranked>(a, b);
  }

  topThree(a: Ranked, b: Ranked, c: Ranked): Ranked {
    return maxABC<Ranked>(a, b, c);
  }
}

export { Caller } from "./caller.js";
export { Ranked$runtimeType } from "./ranked.js";
export type { Ranked } from "./ranked.js";
export { max, maxAB, maxABC } from "./ranks.js";
