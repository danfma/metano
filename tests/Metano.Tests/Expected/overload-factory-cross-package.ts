import { isString } from "metano-runtime";
import { Money } from "lib/lib";
import { Decimal } from "decimal.js";

export class Receipt extends Money {
  protected static readonly byReceiptAmount: unique symbol = Symbol("Receipt(decimal amount)");

  protected static readonly byReceiptCents: unique symbol = Symbol("Receipt(string cents)");

  constructor(overload: typeof Receipt.byReceiptAmount, amount: Decimal);
  constructor(overload: typeof Receipt.byReceiptCents, cents: string);
  constructor(amount: Decimal);
  constructor(cents: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptAmount) {
      const amount = args[1] as Decimal;
      super(amount);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptCents) {
      const cents = args[1] as string;
      super(cents.length);

      return;
    }

    if (args.length === 1 && args[0] instanceof Decimal) {
      const amount = args[0] as Decimal;
      super(amount);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const cents = args[0] as string;
      super(cents.length);

      return;
    }

    throw new Error("No matching constructor");
  }

  static override fromAmount(amount: Decimal): Receipt {
    return new Receipt(Receipt.byReceiptAmount, amount);
  }

  static fromReceiptCents(cents: string): Receipt {
    return new Receipt(Receipt.byReceiptCents, cents);
  }
}
