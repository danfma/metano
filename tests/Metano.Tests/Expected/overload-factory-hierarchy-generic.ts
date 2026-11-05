import { isBool } from "metano-runtime";
import { B } from "./b";

export class C<P, Q> extends B<P, Q> {
  protected static readonly byCKV: unique symbol = Symbol("C(P k, Q v)");

  protected static readonly byCVKFlip: unique symbol = Symbol("C(Q v, P k, bool flip)");

  constructor(overload: typeof C.byCKV, k: P, v: Q);
  constructor(overload: typeof C.byCVKFlip, v: Q, k: P, flip: boolean);
  constructor(v: Q, k: P, flip: boolean);
  constructor(k: P, v: Q);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === C.byCKV) {
      const k = args[1] as P;
      const v = args[2] as Q;
      super(v, k);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === C.byCVKFlip) {
      const v = args[1] as Q;
      const k = args[2] as P;
      const flip = args[3] as boolean;
      super(v, k);

      return;
    }

    if (args.length === 3 && isBool(args[2])) {
      const v = args[0] as Q;
      const k = args[1] as P;
      const flip = args[2] as boolean;
      super(v, k);

      return;
    }

    if (args.length === 2) {
      const k = args[0] as P;
      const v = args[1] as Q;
      super(v, k);

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromCKV<P, Q>(k: P, v: Q): C<P, Q> {
    return new C(C.byCKV, k, v);
  }

  static fromVKFlip<P, Q>(v: Q, k: P, flip: boolean): C<P, Q> {
    return new C(C.byCVKFlip, v, k, flip);
  }
}
