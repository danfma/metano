import { isDecimal, isInt32 } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";

export class Money {
  protected static readonly byMoneyAmount = /* @__PURE__ */ Symbol("Money(decimal amount)");
  protected static readonly byMoneyCents = /* @__PURE__ */ Symbol("Money(int cents)");
  static readonly zero = /* @__PURE__ */ Money.fromCents(0) as Money;
  readonly how: string = "";

  constructor(overload: typeof Money.byMoneyAmount, amount: Decimal);
  constructor(overload: typeof Money.byMoneyCents, cents: number);
  constructor(amount: Decimal);
  constructor(cents: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyAmount) {
      this.how = "amount";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyCents) {
      this.how = "cents";

      return;
    }

    if (args.length === 1 && isDecimal(args[0])) {
      this.how = "amount";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
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
