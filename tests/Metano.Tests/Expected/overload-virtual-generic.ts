import { isInt32 } from "metano-runtime";

export class Repo<T> {
  constructor() { }

  putCount(count: number): string {
    return "count";
  }

  putItem(item: T): string {
    return "item";
  }

  put(count: number): string;
  put(item: T): string;
  put(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.putCount(args[0] as number);
    }

    if (args.length === 1) {
      return this.putItem(args[0] as T);
    }

    throw new Error("No matching overload for put");
  }
}

import { Repo } from "./repo";

export class StrRepo extends Repo<string> {
  constructor() {
    super();
  }

  override putItem(item: string): string {
    return "str:" + item;
  }
}

import { isInt32 } from "metano-runtime";

export class Box<T> {
  constructor() { }

  fInt32(a: number): string {
    return "int";
  }

  fT(a: T): string {
    return "T";
  }

  f(a: number): string;
  f(a: T): string;
  f(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.fInt32(args[0] as number);
    }

    if (args.length === 1) {
      return this.fT(args[0] as T);
    }

    throw new Error("No matching overload for f");
  }
}

import { Box } from "./box";

export class StrBox extends Box<string> {
  constructor() {
    super();
  }

  override fT(a: string): string {
    return "S:" + a + super.fT(a);
  }
}
