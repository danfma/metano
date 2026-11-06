import { isInt32 } from "metano-runtime";

export function pickVN<T>(v: T, n: number): T {
  return v;
}

export function pickV<T>(v: T): T {
  return v;
}

export function pick<T>(v: T, n: number): T;
export function pick<T>(v: T): T;
export function pick(...args: unknown[]): any {
  if (args.length === 2 && isInt32(args[1])) {
    return pickVN(args[0] as any, args[1] as any);
  }

  if (args.length === 1) {
    return pickV(args[0] as any);
  }

  throw new Error("No matching overload for pick");
}

import { pickV, pickVN } from "./greets";

export class Caller {
  constructor() { }

  static one(): string {
    return pickV<string>("a");
  }

  static two(): string {
    return pickVN<string>("a", 2);
  }
}

export { Caller } from "./caller";
export { pick, pickV, pickVN } from "./greets";
