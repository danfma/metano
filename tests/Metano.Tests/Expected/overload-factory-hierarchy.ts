import { isDecimal, isInt32, isInt64, isString } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";

export class Money {
  protected static readonly byMoneyAmount = /* @__PURE__ */ Symbol("Money(decimal amount)");
  protected static readonly byMoneyCents = /* @__PURE__ */ Symbol("Money(int cents)");
  protected static readonly byMoneyCode = /* @__PURE__ */ Symbol("Money(string code)");
  protected static readonly byMoneyId = /* @__PURE__ */ Symbol("Money(long id)");
  declare readonly how: string;

  constructor(overload: typeof Money.byMoneyAmount, amount: Decimal);
  constructor(overload: typeof Money.byMoneyCents, cents: number);
  /** @internal */
  constructor(overload: typeof Money.byMoneyCode, code: string);
  constructor(overload: typeof Money.byMoneyId, id: bigint);
  constructor(amount: Decimal);
  constructor(cents: number);
  /** @internal */
  constructor(code: string);
  constructor(id: bigint);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyAmount) {
      this.how = "amount";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyCents) {
      this.how = "cents";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyCode) {
      this.how = "code";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyId) {
      this.how = "id";

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

    if (args.length === 1 && isString(args[0])) {
      this.how = "code";

      return;
    }

    if (args.length === 1 && isInt64(args[0])) {
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

  static fromId(id: bigint): Money {
    return new Money(Money.byMoneyId, id);
  }
}

import { isDecimal, isInt32, isInt64, isString } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";
import { Money } from "./money.js";

export class Receipt extends Money {
  protected static readonly byReceiptAmount = /* @__PURE__ */ Symbol("Receipt(decimal amount)");
  protected static readonly byReceiptCents = /* @__PURE__ */ Symbol("Receipt(int cents)");
  protected static readonly byReceiptCode = /* @__PURE__ */ Symbol("Receipt(string code)");
  protected static readonly byReceiptId = /* @__PURE__ */ Symbol("Receipt(long id)");

  constructor(overload: typeof Receipt.byReceiptAmount, amount: Decimal);
  constructor(overload: typeof Receipt.byReceiptCents, cents: number);
  constructor(overload: typeof Receipt.byReceiptCode, code: string);
  constructor(overload: typeof Receipt.byReceiptId, id: bigint);
  constructor(amount: Decimal);
  constructor(cents: number);
  constructor(code: string);
  constructor(id: bigint);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptAmount) {
      const amount = args[1] as Decimal;
      super(Money.byMoneyAmount, amount);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptCents) {
      const cents = args[1] as number;
      super(Money.byMoneyCents, cents);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptCode) {
      const code = args[1] as string;
      super(Money.byMoneyCents, code.length);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Receipt.byReceiptId) {
      const id = args[1] as bigint;
      super(Money.byMoneyId, id);

      return;
    }

    if (args.length === 1 && isDecimal(args[0])) {
      const amount = args[0] as Decimal;
      super(Money.byMoneyAmount, amount);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const cents = args[0] as number;
      super(Money.byMoneyCents, cents);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const code = args[0] as string;
      super(Money.byMoneyCents, code.length);

      return;
    }

    if (args.length === 1 && isInt64(args[0])) {
      const id = args[0] as bigint;
      super(Money.byMoneyId, id);

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

  protected static fromReceiptId(id: bigint): Receipt {
    return new Receipt(Receipt.byReceiptId, id);
  }
}

import { isInt32, isString } from "metano-runtime";
import { Receipt } from "./receipt.js";

export class GiftReceipt extends Receipt {
  protected static readonly byGiftReceiptAmount = /* @__PURE__ */ Symbol("GiftReceipt(string amount)");
  protected static readonly byGiftReceiptCents = /* @__PURE__ */ Symbol("GiftReceipt(int cents)");
  declare readonly note: string;

  constructor(overload: typeof GiftReceipt.byGiftReceiptAmount, amount: string);
  constructor(overload: typeof GiftReceipt.byGiftReceiptCents, cents: number);
  constructor(amount: string);
  constructor(cents: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === GiftReceipt.byGiftReceiptAmount) {
      const amount = args[1] as string;
      super(Receipt.byReceiptCents, amount.length);
      this.note = "";
      this.note = amount;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === GiftReceipt.byGiftReceiptCents) {
      const cents = args[1] as number;
      super(Receipt.byReceiptCents, cents);
      this.note = "";

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const amount = args[0] as string;
      super(Receipt.byReceiptCents, amount.length);
      this.note = "";
      this.note = amount;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const cents = args[0] as number;
      super(Receipt.byReceiptCents, cents);
      this.note = "";

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
