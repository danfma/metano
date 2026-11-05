import { isInt32 } from "metano-runtime";
import { C } from "./c";

export class D3 extends C {
  constructor() {
    super();
  }

  override convX(x: number): number;
  override convX(x: string): number;
  override convX(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;

      return 10;
    }

    return super.convX(...args as [never]);
  }
}

import { isInt32 } from "metano-runtime";
import type { IConv } from "./i-conv";

export class C1 implements IConv<number> {
  constructor() { }

  convXN(x: number, n: number): number {
    return 3;
  }

  convX(x: number): number {
    return 1;
  }

  conv(x: number, n: number): number;
  conv(x: number): number;
  conv(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.convXN(args[0] as number, args[1] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.convX(args[0] as number);
    }

    throw new Error("No matching overload for conv");
  }
}

import { isInt32, isString } from "metano-runtime";
import { C1 } from "./c1";
import type { IConv } from "./i-conv";

export class D4 extends C1 implements IConv<string> {
  constructor() {
    super();
  }

  override convXN(x: number, n: number): number;
  override convXN(x: string, n: number): number;
  override convXN(...args: unknown[]): number {
    if (args.length === 2 && isString(args[0]) && isInt32(args[1])) {
      const x = args[0] as string;
      const n = args[1] as number;

      return 4;
    }

    return super.convXN(...args as [never, never]);
  }

  override convX(x: number): number;
  override convX(x: string): number;
  override convX(...args: unknown[]): number {
    if (args.length === 1 && isString(args[0])) {
      const x = args[0] as string;

      return 2;
    }

    return super.convX(...args as [never]);
  }

  override conv(x: number, n: number): number;
  override conv(x: number): number;
  override conv(x: string, n: number): number;
  override conv(x: string): number;
  override conv(...args: unknown[]): number {
    if (args.length === 2 && isString(args[0]) && isInt32(args[1])) {
      return this.convXN(args[0] as string, args[1] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.convX(args[0] as string);
    }

    return super.conv(...args as [never]);
  }
}
