import { valueEquals } from "metano-runtime";
import { Decimal } from "metano-runtime/system/decimal";

export class Uses {
  constructor() { }

  static fromInt(i: number): Decimal {
    return new Decimal(i + 0);
  }

  static half(d: Decimal): Decimal {
    return d.div(new Decimal("2"));
  }

  static argument(s: number): Decimal {
    return Uses.half(new Decimal(s + 0));
  }

  static sum(d: Decimal, b: number): Decimal {
    return d.plus(new Decimal(b + 0));
  }

  static less(u: number, d: Decimal): boolean {
    return new Decimal(u + 0).lt(d);
  }

  static lifted(n: number | null): Decimal | null {
    return n == null ? null : new Decimal(n + 0);
  }

  static coalesce(n: number | null): Decimal {
    return (n == null ? null : new Decimal(n + 0)) ?? new Decimal("1.5");
  }

  static cast(s: number): Decimal {
    return new Decimal(s + 0);
  }

  static compound(d: Decimal, i: number): Decimal {
    d = d.plus(new Decimal(i + 0));
    d = d.minus(new Decimal("1"));
    d = d.times(new Decimal(i + 0));
    d = d.div(new Decimal("4"));
    d = d.mod(new Decimal("3"));

    return d;
  }

  static steps(arr: Decimal[], k: number): Decimal {
    const d = ((receiver$temp$0) => {
      arr[k] = receiver$temp$0.plus(new Decimal("1"));

      return receiver$temp$0;
    })(arr[k]!);
    arr[k] = arr[k]!.minus(new Decimal("1"));

    return d;
  }

  static fromLong(l: bigint): Decimal {
    return new Decimal(l.toString());
  }

  static equal(d: Decimal | null, i: number, l: bigint, c: string): boolean {
    return valueEquals(d, new Decimal(i + 0)) || !valueEquals(d, new Decimal(l.toString())) || valueEquals(d, new Decimal(c.charCodeAt(0)));
  }
}
