import { isInt32 } from "metano-runtime";
import { Decimal } from "decimal.js";

export class Money {
  protected static readonly byMoneyAmount: unique symbol = Symbol("Money(decimal amount)");

  protected static readonly byMoneyCents: unique symbol = Symbol("Money(int cents)");

  static readonly zero: Money = Money.fromCents(0);

  readonly how: string = "";

  constructor(overload: typeof Money.byMoneyAmount, amount: Decimal);
  constructor(overload: typeof Money.byMoneyCents, cents: number);
  constructor(amount: Decimal);
  constructor(cents: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyAmount) {
      const amount = args[1] as Decimal;
      this.how = "amount";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyCents) {
      const cents = args[1] as number;
      this.how = "cents";

      return;
    }

    if (args.length === 1 && args[0] instanceof Decimal) {
      const amount = args[0] as Decimal;
      this.how = "amount";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const cents = args[0] as number;
      this.how = "cents";

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromAmount(amount: Decimal): Money {
    return new Money(Money.byMoneyAmount, amount);
  }

  static fromCents(cents: number): Money {
    return new Money(Money.byMoneyCents, cents);
  }
}
