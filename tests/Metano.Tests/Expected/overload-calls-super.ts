import { isInt32, isString } from "metano-runtime";
import { Base } from "./base";

export class Derived extends Base {
  protected static readonly byDerivedC: unique symbol = Symbol("Derived(int c)");

  protected static readonly byDerivedS: unique symbol = Symbol("Derived(string s)");

  protected static readonly byDerivedDefault: unique symbol = Symbol("Derived()");

  constructor(overload: typeof Derived.byDerivedC, c: number);
  constructor(overload: typeof Derived.byDerivedS, s: string);
  constructor(overload: typeof Derived.byDerivedDefault);
  constructor(c: number);
  constructor(s: string);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Derived.byDerivedC) {
      const c = args[1] as number;
      super(Base.byBaseAB, 1, 5);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Derived.byDerivedS) {
      const s = args[1] as string;
      super(Base.byBaseS, s);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Derived.byDerivedDefault) {
      super(Base.byBaseDefault);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const c = args[0] as number;
      super(Base.byBaseAB, 1, 5);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const s = args[0] as string;
      super(Base.byBaseS, s);

      return;
    }

    if (args.length === 0) {
      super(Base.byBaseDefault);

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromC(c: number): Derived {
    return new Derived(Derived.byDerivedC, c);
  }

  static override fromS(s: string): Derived {
    return new Derived(Derived.byDerivedS, s);
  }

  static override fromDefault(): Derived {
    return new Derived(Derived.byDerivedDefault);
  }
}

import { Base } from "./base";

export class Single extends Base {
  constructor(a: number) {
    super(Base.byBaseAB, a, 7);
  }
}

import { Base } from "./base";

export class Primary extends Base {
  constructor(x: number) {
    super(Base.byBaseAB, x, 5);
  }
}

import { isInt32 } from "metano-runtime";
import { Base } from "./base";

export class Chained extends Base {
  protected static readonly byChainedDefault: unique symbol = Symbol("Chained()");

  protected static readonly byChainedC: unique symbol = Symbol("Chained(int c)");

  constructor(overload: typeof Chained.byChainedDefault);
  constructor(overload: typeof Chained.byChainedC, c: number);
  constructor(c: number);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Chained.byChainedDefault) {
      const __chain0Arg0 = 3;

      {
        const c = __chain0Arg0;
        super(Base.byBaseAB, c, c);
      }

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Chained.byChainedC) {
      const c = args[1] as number;
      super(Base.byBaseAB, c, c);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const c = args[0] as number;
      super(Base.byBaseAB, c, c);

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = 3;

      {
        const c = __chain0Arg0;
        super(Base.byBaseAB, c, c);
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  static override fromDefault(): Chained {
    return new Chained(Chained.byChainedDefault);
  }

  static fromC(c: number): Chained {
    return new Chained(Chained.byChainedC, c);
  }
}

import { Shape } from "./shape";

export class Circle extends Shape {
  constructor(radius: number) {
    super(Shape.byShapeScale, radius);
  }
}
