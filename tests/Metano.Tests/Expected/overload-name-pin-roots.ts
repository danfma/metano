export interface IShape {
  area(s: number): number;
  area(w: number, h: number): number;
  areaSide(s: number): number;
  areaWH(w: number, h: number): number;
}

import { isInt32 } from "metano-runtime";
import type { IShape } from "./i-shape";

export class Square implements IShape {
  constructor() { }

  areaWH(w: number, h: number): number {
    return w * h;
  }

  areaSide(side: number): number {
    return side * side;
  }

  area(w: number, h: number): number;
  area(side: number): number;
  area(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.areaWH(args[0] as number, args[1] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.areaSide(args[0] as number);
    }

    throw new Error("No matching overload for area");
  }
}

import { isInt32, isString } from "metano-runtime";

export class Counter {
  constructor() { }

  addOne(x: number): number {
    return x;
  }

  addS(s: string): number {
    return s.length;
  }

  add(x: number): number;
  add(s: string): number;
  add(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addOne(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.addS(args[0] as string);
    }

    throw new Error("No matching overload for add");
  }
}

import { Counter } from "./counter";

export class Doubler extends Counter {
  constructor() {
    super();
  }

  override addOne(x: number): number {
    return x * 2;
  }
}

import { Counter } from "./counter";

export class Tripler extends Counter {
  constructor() {
    super();
  }

  override addOne(x: number): number {
    return x * 3;
  }
}

import type { Counter } from "./counter";
import type { IShape } from "./i-shape";

export class Use {
  constructor() { }

  run(shape: IShape, counter: Counter): number {
    return shape.areaSide(2) + counter.addOne(3);
  }
}
