import { isInt32 } from "metano-runtime";

export class Pt {
  protected static readonly byPtX: unique symbol = Symbol("Pt(int x)");

  protected static readonly byPtXY: unique symbol = Symbol("Pt(int x, int y)");

  x: number = 0;

  name: string;

  constructor(overload: typeof Pt.byPtX, x: number);
  constructor(overload: typeof Pt.byPtXY, x: number, y: number);
  constructor();
  constructor(x: number, y: number);
  constructor(x: number);
  constructor(...args: unknown[]) {
    if (args.length === 0) {
      this.x = 0;
      this.name = null as never;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Pt.byPtX) {
      const x = args[1] as number;
      this.x = x;
      this.name = "a";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Pt.byPtXY) {
      const x = args[1] as number;
      const y = args[2] as number;
      this.x = x + y;
      this.name = "b";

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      const x = args[0] as number;
      const y = args[1] as number;
      this.x = x + y;
      this.name = "b";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;
      this.x = x;
      this.name = "a";

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromX(x: number): Pt {
    return new Pt(Pt.byPtX, x);
  }

  static fromXY(x: number, y: number): Pt {
    return new Pt(Pt.byPtXY, x, y);
  }
}

import { isInt32 } from "metano-runtime";

export class Tick {
  protected static readonly byTickDefault: unique symbol = Symbol("Tick()");

  protected static readonly byTickN: unique symbol = Symbol("Tick(int n)");

  readonly n: number = 0;

  constructor(overload: typeof Tick.byTickDefault);
  constructor(overload: typeof Tick.byTickN, n: number);
  constructor(n: number);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Tick.byTickDefault) {
      this.n = 7;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Tick.byTickN) {
      const n = args[1] as number;
      this.n = n;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const n = args[0] as number;
      this.n = n;

      return;
    }

    if (args.length === 0) {
      this.n = 7;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromDefault(): Tick {
    return new Tick(Tick.byTickDefault);
  }

  static fromN(n: number): Tick {
    return new Tick(Tick.byTickN, n);
  }
}

import { Pt } from "./pt";

export class Use {
  constructor() { }

  origin(): number {
    return new Pt().x;
  }
}
