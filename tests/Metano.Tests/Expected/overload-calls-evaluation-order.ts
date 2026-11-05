import { Base } from "./base";
import { Calls } from "./calls";

export class Derived extends Base {
  constructor() {
    const __baseArg1 = Calls.f();
    const __baseArg0 = Calls.f();
    super(Base.byBaseAB, __baseArg0, __baseArg1);
  }
}

import { Base } from "./base";
import { Calls } from "./calls";

export class Shop {
  constructor() { }

  static make(): Base {
    return (($arg1, $arg0) => Base.fromAB($arg0, $arg1))(Calls.f(), Calls.f());
  }

  static plain(x: number): Base {
    return Base.fromAB(1, x);
  }
}
