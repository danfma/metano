import { isInt32, isInt64, isString } from "metano-runtime";
import { Decimal } from "decimal.js";

export class Money {
  protected static readonly byMoneyAmount: unique symbol = Symbol("Money(decimal amount)");

  protected static readonly byMoneyCents: unique symbol = Symbol("Money(int cents)");

  protected static readonly byMoneyCode: unique symbol = Symbol("Money(string code)");

  protected static readonly byMoneyId: unique symbol = Symbol("Money(long id)");

  readonly how: string = "";

  constructor(overload: typeof Money.byMoneyAmount, amount: Decimal);
  constructor(overload: typeof Money.byMoneyCents, cents: number);
  /** @internal */
  constructor(overload: typeof Money.byMoneyCode, code: string);
  constructor(overload: typeof Money.byMoneyId, id: number);
  constructor(amount: Decimal);
  constructor(cents: number);
  /** @internal */
  constructor(code: string);
  constructor(id: number);
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

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyCode) {
      const code = args[1] as string;
      this.how = "code";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyId) {
      const id = args[1] as number;
      this.how = "id";

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

    if (args.length === 1 && isString(args[0])) {
      const code = args[0] as string;
      this.how = "code";

      return;
    }

    if (args.length === 1 && isInt64(args[0])) {
      const id = args[0] as number;
      this.how = "id";

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

  private static fromCode(code: string): Money {
    return new Money(Money.byMoneyCode, code);
  }

  static fromId(id: number): Money {
    return new Money(Money.byMoneyId, id);
  }
}

import { isInt32, isInt64, isString } from "metano-runtime";
import { Decimal } from "decimal.js";
import { Money } from "./money";

export class Receipt extends Money {
  protected static readonly byReceiptAmount: unique symbol = Symbol("Receipt(decimal amount)");

  protected static readonly byReceiptCents: unique symbol = Symbol("Receipt(int cents)");

  protected static readonly byReceiptCode: unique symbol = Symbol("Receipt(string code)");

  protected static readonly byReceiptId: unique symbol = Symbol("Receipt(long id)");

  constructor(overload: typeof Receipt.byReceiptAmount, amount: Decimal);
  constructor(overload: typeof Receipt.byReceiptCents, cents: number);
  constructor(overload: typeof Receipt.byReceiptCode, code: string);
  constructor(overload: typeof Receipt.byReceiptId, id: number);
  constructor(amount: Decimal);
  constructor(cents: number);
  constructor(code: string);
  constructor(id: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptAmount) {
      const amount = args[1] as Decimal;
      super(amount);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptCents) {
      const cents = args[1] as number;
      super(cents);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptCode) {
      const code = args[1] as string;
      super(code.length);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptId) {
      const id = args[1] as number;
      super(id);

      return;
    }

    if (args.length === 1 && args[0] instanceof Decimal) {
      const amount = args[0] as Decimal;
      super(amount);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const cents = args[0] as number;
      super(cents);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const code = args[0] as string;
      super(code.length);

      return;
    }

    if (args.length === 1 && isInt64(args[0])) {
      const id = args[0] as number;
      super(id);

      return;
    }

    throw new Error("No matching constructor");
  }

  static override fromAmount(amount: Decimal): Receipt {
    return new Receipt(Receipt.byReceiptAmount, amount);
  }

  static override fromCents(cents: number): Receipt {
    return new Receipt(Receipt.byReceiptCents, cents);
  }

  static fromReceiptCode(code: string): Receipt {
    return new Receipt(Receipt.byReceiptCode, code);
  }

  protected static fromReceiptId(id: number): Receipt {
    return new Receipt(Receipt.byReceiptId, id);
  }
}

import { isInt32, isString } from "metano-runtime";
import { Receipt } from "./receipt";

export class GiftReceipt extends Receipt {
  protected static readonly byGiftReceiptAmount: unique symbol = Symbol("GiftReceipt(string amount)");

  protected static readonly byGiftReceiptCents: unique symbol = Symbol("GiftReceipt(int cents)");

  readonly note: string = "";

  constructor(overload: typeof GiftReceipt.byGiftReceiptAmount, amount: string);
  constructor(overload: typeof GiftReceipt.byGiftReceiptCents, cents: number);
  constructor(amount: string);
  constructor(cents: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === GiftReceipt.byGiftReceiptAmount) {
      const amount = args[1] as string;
      super(amount.length);
      this.note = amount;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === GiftReceipt.byGiftReceiptCents) {
      const cents = args[1] as number;
      super(cents);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const amount = args[0] as string;
      super(amount.length);
      this.note = amount;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const cents = args[0] as number;
      super(cents);

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromGiftReceiptAmount(amount: string): GiftReceipt {
    return new GiftReceipt(GiftReceipt.byGiftReceiptAmount, amount);
  }

  static override fromCents(cents: number): GiftReceipt {
    return new GiftReceipt(GiftReceipt.byGiftReceiptCents, cents);
  }
}
