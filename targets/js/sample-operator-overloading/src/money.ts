import { HashCode, isBigInt } from "metano-runtime";
import { Decimal } from "decimal.js";
import type { Currency } from "./currency";
import { NoSameMoneyCurrencyException } from "./no-same-money-currency-exception";

export class Money {
  constructor(readonly cents: bigint, readonly currency: Currency) { }

  static __add(left: Money, right: Money): Money {
    Money.expectSameCurrency(left, right);

    return left.with({ cents: left.cents + right.cents });
  }

  $add(right: Money): Money {
    return Money.__add(this, right);
  }

  static __subtract(left: Money, right: Money): Money {
    Money.expectSameCurrency(left, right);

    return left.with({ cents: left.cents - right.cents });
  }

  $subtract(right: Money): Money {
    return Money.__subtract(this, right);
  }

  private static __multiplyMoneyBigInteger(left: Money, factor: bigint): Money {
    return left.with({ cents: left.cents * factor });
  }

  private static __multiplyMoneyMoney(left: Money, right: Money): Money {
    Money.expectSameCurrency(left, right);

    return left.with({ cents: left.cents * right.cents });
  }

  static __multiply(left: Money, factor: bigint): Money;
  static __multiply(left: Money, right: Money): Money;
  static __multiply(...args: unknown[]): Money {
    if (args.length === 2 && args[0] instanceof Money && isBigInt(args[1])) {
      return Money.__multiplyMoneyBigInteger(args[0] as Money, args[1] as bigint);
    }

    if (args.length === 2 && args[0] instanceof Money && args[1] instanceof Money) {
      return Money.__multiplyMoneyMoney(args[0] as Money, args[1] as Money);
    }

    throw new Error("No matching overload for multiply");
  }

  $multiply(factor: bigint): Money;
  $multiply(right: Money): Money;
  $multiply(...args: unknown[]): Money {
    if (args.length === 1 && isBigInt(args[0])) {
      return Money.__multiplyMoneyBigInteger(this, args[0] as bigint);
    }

    if (args.length === 1 && args[0] instanceof Money) {
      return Money.__multiplyMoneyMoney(this, args[0] as Money);
    }

    throw new Error("No matching overload for multiply");
  }

  private static __divideMoneyBigInteger(left: Money, divisor: bigint): Money {
    return left.with({ cents: left.cents / divisor });
  }

  private static __divideMoneyMoney(left: Money, divisor: Money): Money {
    Money.expectSameCurrency(left, divisor);

    return left.with({ cents: left.cents / divisor.cents });
  }

  static __divide(left: Money, divisor: bigint): Money;
  static __divide(left: Money, divisor: Money): Money;
  static __divide(...args: unknown[]): Money {
    if (args.length === 2 && args[0] instanceof Money && isBigInt(args[1])) {
      return Money.__divideMoneyBigInteger(args[0] as Money, args[1] as bigint);
    }

    if (args.length === 2 && args[0] instanceof Money && args[1] instanceof Money) {
      return Money.__divideMoneyMoney(args[0] as Money, args[1] as Money);
    }

    throw new Error("No matching overload for divide");
  }

  $divide(divisor: bigint): Money;
  $divide(divisor: Money): Money;
  $divide(...args: unknown[]): Money {
    if (args.length === 1 && isBigInt(args[0])) {
      return Money.__divideMoneyBigInteger(this, args[0] as bigint);
    }

    if (args.length === 1 && args[0] instanceof Money) {
      return Money.__divideMoneyMoney(this, args[0] as Money);
    }

    throw new Error("No matching overload for divide");
  }

  toDecimal(): Decimal {
    return new Decimal(this.cents.toString()).div(new Decimal("100"));
  }

  toString(): string {
    return `${this.toDecimal()} ${this.currency.toString().toUpperCase()}`;
  }

  static fromCents(cents: bigint, currency: Currency): Money {
    return new Money(cents, currency);
  }

  static fromValue(amount: Decimal, currency: Currency): Money {
    return new Money(BigInt(amount.times(new Decimal("100")).round().toFixed(0)), currency);
  }

  private static expectSameCurrency(left: Money, right: Money): void {
    if (left.currency !== right.currency) {
      throw new NoSameMoneyCurrencyException(left.currency, right.currency);
    }
  }

  equals(other: any): boolean {
    return other instanceof Money && this.cents === other.cents && this.currency === other.currency;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.cents);
    hc.add(this.currency);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Money>): Money {
    return new Money(overrides?.cents ?? this.cents, overrides?.currency ?? this.currency);
  }
}
