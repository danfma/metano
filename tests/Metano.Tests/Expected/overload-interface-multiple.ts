export interface IConv<T> {
  conv(x: T): number;
  conv(x: T, n: number): number;
  convX(x: T): number;
  convXN(x: T, n: number): number;
}

import { isInt32, isString } from "metano-runtime";
import type { IConv } from "./i-conv";

export class C implements IConv<number>, IConv<string> {
  constructor() { }

  convXN(x: number, n: number): number;
  convXN(x: string, n: number): number;
  convXN(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      const x = args[0] as number;
      const n = args[1] as number;

      return 3;
    }

    if (args.length === 2 && isString(args[0]) && isInt32(args[1])) {
      const x = args[0] as string;
      const n = args[1] as number;

      return 4;
    }

    throw new Error("No matching overload for convXN");
  }

  convX(x: number): number;
  convX(x: string): number;
  convX(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;

      return 1;
    }

    if (args.length === 1 && isString(args[0])) {
      const x = args[0] as string;

      return 2;
    }

    throw new Error("No matching overload for convX");
  }

  conv(x: number, n: number): number;
  conv(x: string, n: number): number;
  conv(x: number): number;
  conv(x: string): number;
  conv(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.convXN(args[0] as number, args[1] as number);
    }

    if (args.length === 2 && isString(args[0]) && isInt32(args[1])) {
      return this.convXN(args[0] as string, args[1] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.convX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.convX(args[0] as string);
    }

    throw new Error("No matching overload for conv");
  }
}
