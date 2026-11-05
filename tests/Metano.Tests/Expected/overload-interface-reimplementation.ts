import { isBool, isInt32, isString } from "metano-runtime";
import type { IR } from "./ir";

export class X implements IR {
  constructor() { }

  rX(x: number): number {
    return 1;
  }

  rS(s: string): number {
    return 2;
  }

  rB(b: boolean): number {
    return 3;
  }

  r(x: number): number;
  r(s: string): number;
  r(b: boolean): number;
  r(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.rX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.rS(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.rB(args[0] as boolean);
    }

    throw new Error("No matching overload for r");
  }
}

import { isFloat64 } from "metano-runtime";
import { X } from "./x";

export class X4 extends X {
  constructor() {
    super();
  }

  rD(d: number): number {
    return 4;
  }

  override r(x: number): number;
  override r(s: string): number;
  override r(b: boolean): number;
  override r(d: number): number;
  override r(...args: unknown[]): number {
    if (args.length === 1 && isFloat64(args[0])) {
      return this.rD(args[0] as number);
    }

    return super.r(...args as [never]);
  }
}

import { isInt32 } from "metano-runtime";
import type { IR } from "./ir";
import { X } from "./x";

export class X3 extends X implements IR {
  constructor() {
    super();
  }

  override rX(x: number): number {
    return 9;
  }

  override r(s: string): number;
  override r(b: boolean): number;
  override r(x: number): number;
  override r(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.rX(args[0] as number);
    }

    return super.r(...args as [never]);
  }
}

import { isInt32, isString } from "metano-runtime";
import type { IR } from "./ir";

export class Bn implements IR {
  constructor() { }

  rX(x: number): number {
    return 1;
  }

  rS(s: string): number {
    return 2;
  }

  r(x: number): number;
  r(s: string): number;
  r(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.rX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.rS(args[0] as string);
    }

    throw new Error("No matching overload for r");
  }
}

import { isInt32 } from "metano-runtime";
import { Bn } from "./bn";
import type { IR } from "./ir";

export class Dn extends Bn implements IR {
  constructor() {
    super();
  }

  override rX(x: number): number {
    return 10;
  }

  override r(s: string): number;
  override r(x: number): number;
  override r(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.rX(args[0] as number);
    }

    return super.r(...args as [never]);
  }
}

import { isBool, isInt32, isString } from "metano-runtime";
import type { IB } from "./ib";
import type { IR } from "./ir";

export class Y implements IR, IB {
  constructor() { }

  rX(x: number): number {
    return 1;
  }

  rY(x: number): number {
    return this.rX(x);
  }

  rS(s: string): number {
    return 2;
  }

  rF(f: boolean): number {
    return 3;
  }

  r(x: number): number;
  r(s: string): number;
  r(f: boolean): number;
  r(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.rX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.rS(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.rF(args[0] as boolean);
    }

    throw new Error("No matching overload for r");
  }
}

import { isInt32 } from "metano-runtime";
import type { IB } from "./ib";
import type { IR } from "./ir";
import { Y } from "./y";

export class Y3 extends Y implements IR, IB {
  constructor() {
    super();
  }

  override rX(x: number): number {
    return 9;
  }

  override rY(x: number): number {
    return this.rX(x);
  }

  override r(s: string): number;
  override r(f: boolean): number;
  override r(x: number): number;
  override r(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.rX(args[0] as number);
    }

    return super.r(...args as [never]);
  }
}
