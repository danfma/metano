import { isInt32, isInt64, isString, isUInt64 } from "metano-runtime";
import { Point } from "./point";

export class Names {
  constructor() { }

  private addXY(x: number, y: number): number {
    return x + y;
  }

  private addP(p: Point): number {
    return p.x;
  }

  add(x: number, y: number): number;
  add(p: Point): number;
  add(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.addXY(args[0] as number, args[1] as number);
    }

    if (args.length === 1 && args[0] instanceof Point) {
      return this.addP(args[0] as Point);
    }

    throw new Error("No matching overload for add");
  }

  private fInt32(a: number): string {
    return "int";
  }

  private fString(a: string): string {
    return "string";
  }

  f(a: number): string;
  f(a: string): string;
  f(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.fInt32(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.fString(args[0] as string);
    }

    throw new Error("No matching overload for f");
  }

  private gInt32(v: number): string {
    return "int";
  }

  private gInt64(v: number): string {
    return "long";
  }

  g(v: number): string;
  g(v: number): string;
  g(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.gInt32(args[0] as number);
    }

    if (args.length === 1 && isInt64(args[0])) {
      return this.gInt64(args[0] as number);
    }

    throw new Error("No matching overload for g");
  }

  private uInt32(a: number): string {
    return "int";
  }

  private uUInt32(a: number): string {
    return "uint";
  }

  u(a: number): string;
  u(a: number): string;
  u(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.uInt32(args[0] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.uUInt32(args[0] as number);
    }

    throw new Error("No matching overload for u");
  }

  private wInt64(a: number): string {
    return "long";
  }

  private wUInt64(a: number): string {
    return "ulong";
  }

  w(a: number): string;
  w(a: number): string;
  w(...args: unknown[]): string {
    if (args.length === 1 && isInt64(args[0])) {
      return this.wInt64(args[0] as number);
    }

    if (args.length === 1 && isUInt64(args[0])) {
      return this.wUInt64(args[0] as number);
    }

    throw new Error("No matching overload for w");
  }

  private lListOfInt32(xs: number[]): number {
    return 1;
  }

  private lInt32Array(xs: number[]): number {
    return 2;
  }

  l(xs: number[]): number;
  l(xs: number[]): number;
  l(...args: unknown[]): number {
    if (args.length === 1 && Array.isArray(args[0])) {
      return this.lListOfInt32(args[0] as number[]);
    }

    if (args.length === 1 && Array.isArray(args[0])) {
      return this.lInt32Array(args[0] as number[]);
    }

    throw new Error("No matching overload for l");
  }

  private sumInt32Array(xs: number[]): number {
    return 1;
  }

  private sumDoubleArray(xs: number[]): number {
    return 2;
  }

  sum(xs: number[]): number;
  sum(xs: number[]): number;
  sum(...args: unknown[]): number {
    if (args.length === 1 && Array.isArray(args[0])) {
      return this.sumInt32Array(args[0] as number[]);
    }

    if (args.length === 1 && Array.isArray(args[0])) {
      return this.sumDoubleArray(args[0] as number[]);
    }

    throw new Error("No matching overload for sum");
  }

  private hInt32OrNull(maybe: number | null): string {
    return "int";
  }

  private hStringOrNull(maybe: string | null): string {
    return "string";
  }

  h(maybe: number | null): string;
  h(maybe: string | null): string;
  h(...args: unknown[]): string {
    if (args.length === 1 && (args[0] === null || isInt32(args[0]))) {
      return this.hInt32OrNull(args[0] as number | null);
    }

    if (args.length === 1 && (args[0] === null || isString(args[0]))) {
      return this.hStringOrNull(args[0] as string | null);
    }

    throw new Error("No matching overload for h");
  }

  private formatFmtCount(fmt: string, count: number): string {
    return fmt;
  }

  private formatFmtArgs(fmt: string, ...args: Object[]): string {
    return fmt;
  }

  format(fmt: string, count: number): string;
  format(fmt: string, ...args: Object[]): string;
  format(...args: unknown[]): string {
    if (args.length === 2 && isString(args[0]) && isInt32(args[1])) {
      return this.formatFmtCount(args[0] as string, args[1] as number);
    }

    if (args.length >= 1 && isString(args[0]) && args.slice(1).every((value) => typeof value === "object")) {
      return this.formatFmtArgs(args[0] as string, ...args.slice(1) as Object[]);
    }

    throw new Error("No matching overload for format");
  }

  private identityString(value: string): string {
    return value;
  }

  private identityT<T>(value: T): T {
    return value;
  }

  identity(value: string): string;
  identity<T>(value: T): T;
  identity(...args: unknown[]): any {
    if (args.length === 1 && isString(args[0])) {
      return this.identityString(args[0] as string);
    }

    if (args.length === 1) {
      return this.identityT(args[0] as any);
    }

    throw new Error("No matching overload for identity");
  }
}
