import { isInt32 } from "metano-runtime";
import { Decimal } from "decimal.js";

export class Cash {
  protected static readonly byCashAmount: unique symbol = Symbol("Cash(decimal amount)");

  protected static readonly byCashCents: unique symbol = Symbol("Cash(int cents)");

  readonly how: string = "";

  constructor(overload: typeof Cash.byCashAmount, amount: Decimal);
  constructor(overload: typeof Cash.byCashCents, cents: number);
  constructor(amount: Decimal);
  constructor(cents: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Cash.byCashAmount) {
      const amount = args[1] as Decimal;
      this.how = "amount";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Cash.byCashCents) {
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

  static fromAmount(amount: Decimal): Cash {
    return new Cash(Cash.byCashAmount, amount);
  }

  static fromCents(cents: number): Cash {
    return new Cash(Cash.byCashCents, cents);
  }

  private static fromAmount$(amount: Decimal): Cash {
    return Cash.fromAmount(amount);
  }

  private static fromCents$(cents: number): Cash {
    return Cash.fromCents(cents);
  }

  static from(amount: Decimal): Cash;
  static from(cents: number): Cash;
  static from(...args: unknown[]): Cash {
    if (args.length === 1 && args[0] instanceof Decimal) {
      return Cash.fromAmount$(args[0] as Decimal);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return Cash.fromCents$(args[0] as number);
    }

    throw new Error("No matching overload for from");
  }
}

import { isInt32, isString } from "metano-runtime";

export class Fee {
  protected static readonly byFeeCents$: unique symbol = Symbol("Fee(int cents)");

  protected static readonly byFeeLabel: unique symbol = Symbol("Fee(string label)");

  static fromCents: number = 3;

  constructor(overload: typeof Fee.byFeeCents$, cents: number);
  constructor(overload: typeof Fee.byFeeLabel, label: string);
  constructor(cents: number);
  constructor(label: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Fee.byFeeCents$) {
      const cents = args[1] as number;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Fee.byFeeLabel) {
      const label = args[1] as string;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const cents = args[0] as number;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const label = args[0] as string;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromCents$(cents: number): Fee {
    return new Fee(Fee.byFeeCents$, cents);
  }

  static fromLabel(label: string): Fee {
    return new Fee(Fee.byFeeLabel, label);
  }
}
