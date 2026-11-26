import { Base } from "./base.js";
import { Calls } from "./calls.js";

export class Derived extends Base {
  constructor() {
    const __baseArg1 = Calls.f();
    const __baseArg0 = Calls.f();
    super(Base.byBaseAB, __baseArg0, __baseArg1);
  }
}

import { Base } from "./base.js";
import { Calls } from "./calls.js";

export class Shop {
  private constructor() { }

  static make(): Base {
    let $arg1, $arg0;

    return ($arg1 = Calls.f(), $arg0 = Calls.f(), Base.fromAB($arg0, $arg1));
  }

  static plain(x: number): Base {
    return Base.fromAB(1, x);
  }
}
