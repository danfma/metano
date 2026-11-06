import { isInt32, isString } from "metano-runtime";

export class Extra {
  constructor() { }

  pS(s: string): number {
    return s.length;
  }

  pARest(a: number, ...rest: number[]): number {
    return a + rest.length;
  }

  p(s: string): number;
  p(a: number, ...rest: number[]): number;
  p(...args: unknown[]): number {
    if (args.length === 1 && isString(args[0])) {
      return this.pS(args[0] as string);
    }

    if (args.length >= 1 && isInt32(args[0]) && args.slice(1).every((value) => isInt32(value))) {
      return this.pARest(args[0] as number, ...args.slice(1) as number[]);
    }

    throw new Error("No matching overload for p");
  }

  mS(s: string): number {
    return s.length;
  }

  mABRest(a: number, b: number = 2, ...rest: number[]): number {
    return a + b + rest.length;
  }

  m(s: string): number;
  m(a: number, b?: number, ...rest: number[]): number;
  m(...args: unknown[]): number {
    if (args.length === 1 && isString(args[0])) {
      return this.mS(args[0] as string);
    }

    if (args.length >= 1 && isInt32(args[0]) && (args[1] === undefined || isInt32(args[1])) && args.slice(2).every((value) => isInt32(value))) {
      return this.mABRest(args[0] as number, args[1] as number, ...args.slice(2) as number[]);
    }

    throw new Error("No matching overload for m");
  }

  unqualified(a: number): number {
    return this.pARest(a, 8, 9);
  }

  conditional(other: Extra | null): number {
    return other?.pARest(1, 2) ?? -1;
  }
}

import type { Extra } from "./extra";

export class Use {
  constructor() { }

  expanded(e: Extra): number {
    return e.pARest(1, 2, 3);
  }

  pair(e: Extra): number {
    return e.pARest(5, 6);
  }

  alone(e: Extra): number {
    return e.pARest(7);
  }

  spread(e: Extra, items: number[]): number {
    return e.pARest(1, ...items);
  }

  ofString(e: Extra): number {
    return e.pS("abcd");
  }

  defaults(e: Extra): number {
    return e.mABRest(1, 2);
  }

  oneRest(e: Extra): number {
    return e.mABRest(1, 3, 4);
  }

  ofStringM(e: Extra): number {
    return e.mS("ab");
  }
}
