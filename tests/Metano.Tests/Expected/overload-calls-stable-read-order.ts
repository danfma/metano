import type { Engine } from "./engine.js";
import { Holder } from "./holder.js";
import type { Point } from "./point.js";

export class PlainCalls {
  private constructor() { }

  static insideOneArgument(e: Engine, h: Holder): number {
    return e.applyFD(7, (h.p - h.q) | 0);
  }

  static getterAndConstant(e: Engine, h: Holder): number {
    return e.applyFD(7, h.p);
  }

  static autoAndField(e: Engine, h: Holder): number {
    return e.applyFD(h.field, h.auto);
  }

  static textOperands(e: Engine, s: string, n: number): number {
    return e.joinFD((s ?? "") + "!", (s ?? "") + n);
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
