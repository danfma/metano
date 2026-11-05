import { HashCode, copyRecordWith, isInt32 } from "metano-runtime";

export class Rec {
  protected static readonly byRecX: unique symbol = Symbol("Rec(int X)");

  protected static readonly byRecDefault: unique symbol = Symbol("Rec()");

  readonly x: number;

  constructor(overload: typeof Rec.byRecX, x: number);
  constructor(overload: typeof Rec.byRecDefault);
  constructor(x: number);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Rec.byRecX) {
      const x = args[1] as number;
      this.x = x;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Rec.byRecDefault) {
      const __chain0Arg0 = 1;

      {
        const x = __chain0Arg0;
        this.x = x;
      }

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;
      this.x = x;

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = 1;

      {
        const x = __chain0Arg0;
        this.x = x;
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromX(x: number): Rec {
    return new Rec(Rec.byRecX, x);
  }

  static fromDefault(): Rec {
    return new Rec(Rec.byRecDefault);
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Rec>): Rec {
    return copyRecordWith<Rec>(this, overrides);
  }
}
