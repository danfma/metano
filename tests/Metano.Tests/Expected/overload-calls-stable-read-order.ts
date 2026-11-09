import type { Engine } from "./engine";
import { Holder } from "./holder";
import type { Point } from "./point";

export class PlainCalls {
  constructor() { }

  static insideOneArgument(e: Engine, h: Holder): number {
    return e.applyFD(7, h.p - h.q);
  }

  static getterAndConstant(e: Engine, h: Holder): number {
    return e.applyFD(7, h.p);
  }

  static autoAndField(e: Engine, h: Holder): number {
    return e.applyFD(h.field, h.auto);
  }

  static textOperands(e: Engine, s: string, n: number): number {
    return e.joinFD(s + "!", s + n);
  }

  static staticAutos(e: Engine): number {
    return e.applyFD(Holder.shared, Holder.shared);
  }

  static positionalRecord(e: Engine, p: Point): number {
    return e.applyFD(p.y, p.x);
  }

  static inParameterOrder(e: Engine, h: Holder): number {
    return e.applyFD(h.p, h.q);
  }
}
