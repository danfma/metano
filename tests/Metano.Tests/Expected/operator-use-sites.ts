import type { Decimal } from "metano-runtime/system/decimal";
import { Money } from "./money.js";
import { Pt } from "./pt.js";

export class Uses {
  private constructor() { }

  static arithmetic(a: Money, b: Money): Money {
    return Money.__add(Money.__add(Money.__add(a, b), Money.__negate(a)), Money.__implicitFromInt32(3));
  }

  static equality(a: Money, b: Money | null): boolean {
    return Money.__equals(a, b) || b == null || null != a;
  }

  static store(m: Money, arr: Money[], i: number): bigint {
    m = Money.__add(m, Money.__implicitFromInt32(2));
    m = Money.__increment(m);
    const before = ((receiver$temp$0) => {
      m = Money.__increment(receiver$temp$0);

      return receiver$temp$0;
    })(m);
    ((receiver$temp$1) => arr[receiver$temp$1] = Money.__add(arr[receiver$temp$1]!, m))(((i = (i + 1) | 0) - 1) | 0);

    return Money.__toInt64(before);
  }

  static toDecimal(m: Money): Decimal {
    let d = Money.__implicitToDecimal(m);
    d = d.plus(Money.__implicitToDecimal(m));

    return d;
  }

  static lifted(a: Pt | null, b: Pt, c: Pt | null): Pt | null {
    return (a == null ? c == null : c != null && Pt.__equals(a, c)) ? a == null ? null : Pt.__add(a, b) : null;
  }

  static sum(raw: number[]): Money {
    let total = Money.__implicitFromInt32(0);
    for (const m$element of raw) {
      const m = Money.__implicitFromInt32(m$element);
      total = Money.__add(total, m);
    }

    return total;
  }
}
