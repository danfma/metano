export interface IShape {
  area(s: number): number;
  area(w: number, h: number): number;
  areaS(s: number): number;
  areaWH(w: number, h: number): number;
}

import { isInt32 } from "metano-runtime";
import type { IShape } from "./i-shape";

export class Sq implements IShape {
  constructor() { }

  areaWH(w: number, h: number): number {
    return w * h;
  }

  areaS(side: number): number {
    return side * side;
  }

  area(w: number, h: number): number;
  area(side: number): number;
  area(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.areaWH(args[0] as number, args[1] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.areaS(args[0] as number);
    }

    throw new Error("No matching overload for area");
  }
}

import { isBool, isInt32 } from "metano-runtime";
import type { IShape } from "./i-shape";

export class Plot implements IShape {
  constructor() { }

  areaWH(w: number, h: number): number {
    return w * h;
  }

  areaS(s: number): number {
    return s * s;
  }

  areaBig(big: boolean): number {
    return big ? 1 : 0;
  }

  area(w: number, h: number): number;
  area(s: number): number;
  area(big: boolean): number;
  area(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.areaWH(args[0] as number, args[1] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.areaS(args[0] as number);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.areaBig(args[0] as boolean);
    }

    throw new Error("No matching overload for area");
  }
}

export interface IA {
  g(a: number): number;
  g(s: string): number;
  gA(a: number): number;
  gS(s: string): number;
}

export interface IB {
  g(b: number): number;
  g(f: boolean): number;
  gB(b: number): number;
  gF(f: boolean): number;
}

import { isBool, isInt32, isString } from "metano-runtime";
import type { IA } from "./ia";
import type { IB } from "./ib";

export class Both implements IA, IB {
  constructor() { }

  gA(x: number): number {
    return x + 1;
  }

  gB(x: number): number {
    return this.gA(x);
  }

  gS(s: string): number {
    return s.length;
  }

  gF(f: boolean): number {
    return f ? 10 : 20;
  }

  g(x: number): number;
  g(s: string): number;
  g(f: boolean): number;
  g(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.gA(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.gS(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.gF(args[0] as boolean);
    }

    throw new Error("No matching overload for g");
  }
}
