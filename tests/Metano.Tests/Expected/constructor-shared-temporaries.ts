import { Base } from "./base.js";
import { Box } from "./box.js";

export class Single extends Base {
  declare readonly kept: Box;

  constructor(p: number) {
    let receiver$temp$0, receiver$temp$0_1;
    super(Base.byBaseBox, (receiver$temp$0 = new Box(), receiver$temp$0.v = p, receiver$temp$0));
    this.kept = undefined!;
    this.kept = (receiver$temp$0_1 = new Box(), receiver$temp$0_1.v = (p + 1) | 0, receiver$temp$0_1);
  }
}

import { Base } from "./base.js";
import { Order } from "./order.js";

export class Reordered extends Base {
  declare readonly q: number;

  constructor() {
    let $arg1, $arg0, $arg1_1, $arg0_1;
    super(Base.byBaseValue, ($arg1 = Order.g(1), $arg0 = Order.g(2), Order.f($arg0, $arg1)));
    this.q = 0;
    this.q = ($arg1_1 = Order.g(3), $arg0_1 = Order.g(4), Order.f($arg0_1, $arg1_1));
  }
}

import { isInt32, isString } from "metano-runtime";
import { Base } from "./base.js";
import { Box } from "./box.js";
import { Order } from "./order.js";

export class Overloaded extends Base {
  protected static readonly byOverloadedP = /* @__PURE__ */ Symbol("Overloaded(int p)");
  protected static readonly byOverloadedS = /* @__PURE__ */ Symbol("Overloaded(string s)");
  declare readonly kept: Box;

  constructor(overload: typeof Overloaded.byOverloadedP, p: number);
  constructor(overload: typeof Overloaded.byOverloadedS, s: string);
  constructor(p: number);
  constructor(s: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Overloaded.byOverloadedP) {
      const p = args[1] as number;
      let receiver$temp$0, receiver$temp$0_1;
      super(Base.byBaseBox, (receiver$temp$0 = new Box(), receiver$temp$0.v = p, receiver$temp$0));
      this.kept = undefined!;
      this.kept = (receiver$temp$0_1 = new Box(), receiver$temp$0_1.v = (p + 1) | 0, receiver$temp$0_1);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Overloaded.byOverloadedS) {
      const s = args[1] as string;
      let $arg1, $arg0, $arg1_1, $arg0_1;
      super(Base.byBaseValue, ($arg1 = Order.g(1), $arg0 = s.length, Order.f($arg0, $arg1)));
      this.kept = undefined!;
      const receiver$temp$0 = new Box();
      receiver$temp$0.v = ($arg1_1 = Order.g(3), $arg0_1 = Order.g(4), Order.f($arg0_1, $arg1_1));
      this.kept = receiver$temp$0;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const p = args[0] as number;
      let receiver$temp$0, receiver$temp$0_1;
      super(Base.byBaseBox, (receiver$temp$0 = new Box(), receiver$temp$0.v = p, receiver$temp$0));
      this.kept = undefined!;
      this.kept = (receiver$temp$0_1 = new Box(), receiver$temp$0_1.v = (p + 1) | 0, receiver$temp$0_1);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const s = args[0] as string;
      let $arg1, $arg0, $arg1_1, $arg0_1;
      super(Base.byBaseValue, ($arg1 = Order.g(1), $arg0 = s.length, Order.f($arg0, $arg1)));
      this.kept = undefined!;
      const receiver$temp$0 = new Box();
      receiver$temp$0.v = ($arg1_1 = Order.g(3), $arg0_1 = Order.g(4), Order.f($arg0_1, $arg1_1));
      this.kept = receiver$temp$0;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromP(p: number): Overloaded {
    return new Overloaded(Overloaded.byOverloadedP, p);
  }

  static fromS(s: string): Overloaded {
    return new Overloaded(Overloaded.byOverloadedS, s);
  }
}
