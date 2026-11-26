import { throwError } from "metano-runtime";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

export class Money {
  declare cents: number;

  constructor(cents: number) {
    this.cents = cents;
  }

  static __implicitFromInt32(c: number): Money {
    return new Money(c);
  }

  static __implicitFromInt64OrNull(c: bigint | null): Money | null {
    return c == null ? null : new Money(Number(BigInt.asIntN(32, c ?? throwError(new InvalidOperationException("Nullable object must have a value.")))));
  }

  static __implicitToString(m: Money): string {
    return "$" + m.cents;
  }

  static __toInt32(m: Money): number {
    return m.cents;
  }

  static __toInt64(m: Money): bigint {
    return BigInt.asIntN(64, BigInt(m.cents) * 1000n);
  }

  static __checkedToInt32(m: Money): number {
    return m.cents * 1;
  }
}
