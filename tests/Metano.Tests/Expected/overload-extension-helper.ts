import { isInt32, isString } from "metano-runtime";
import { Item } from "./item";

export function twiceInt32(item: Item, x: number): number {
  return x * 2;
}

export function twiceString(item: Item, x: string): number {
  return x.length * 2;
}

export function twice(item: Item, x: number): number;
export function twice(item: Item, x: string): number;
export function twice(...args: unknown[]): number {
  if (args.length === 2 && args[0] instanceof Item && isInt32(args[1])) {
    return twiceInt32(args[0] as Item, args[1] as number);
  }

  if (args.length === 2 && args[0] instanceof Item && isString(args[1])) {
    return twiceString(args[0] as Item, args[1] as string);
  }

  throw new Error("No matching overload for twice");
}

import { twiceInt32, twiceString } from "./ext";
import type { Item } from "./item";

export class Use {
  constructor() { }

  a(item: Item): number {
    return twiceInt32(item, 1);
  }

  b(item: Item): number {
    return twiceString(item, "ab");
  }

  c(item: Item | null): number {
    return (item == null ? null : twiceInt32(item, 1)) ?? 0;
  }
}
