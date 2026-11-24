import { isInt32 } from "metano-runtime";
import { Base } from "./base.js";

export class Der extends Base {
  constructor() {
    super();
  }

  static override __toInt32(d: Der): number {
    return Math.imul(d.v, 2);
  }

  static __addDerDer(a: Der, b: Der): Der {
    return new Der();
  }

  static __addDerInt32(a: Der, n: number): Der {
    return new Der();
  }

  static override __add(a: Der, b: Der): Der;
  static override __add(a: Der, n: number): Der;
  static override __add(...args: unknown[]): Der {
    if (args.length === 2 && args[0] instanceof Der && args[1] instanceof Der) {
      return Der.__addDerDer(args[0] as Der, args[1] as Der);
    }

    if (args.length === 2 && args[0] instanceof Der && isInt32(args[1])) {
      return Der.__addDerInt32(args[0] as Der, args[1] as number);
    }

    throw new Error("No matching overload for add");
  }

  override $add(b: Der): Der;
  override $add(n: number): Der;
  override $add(...args: unknown[]): Der {
    if (args.length === 1 && args[0] instanceof Der) {
      return Der.__addDerDer(this, args[0] as Der);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return Der.__addDerInt32(this, args[0] as number);
    }

    throw new Error("No matching overload for add");
  }

  static __subtract(a: Der, b: Der): Der {
    return new Der();
  }

  $subtract(b: Der): Der {
    return Der.__subtract(this, b);
  }

  static override __implicitFromInt32(v: number): Der {
    return new Der();
  }
}
