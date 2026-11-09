import { isInt32, isString } from "metano-runtime";

export class Fee {
  protected static readonly byFeeText: unique symbol = Symbol("Fee(string text)");

  protected static readonly byFeeCents$: unique symbol = Symbol("Fee(int value)");

  readonly cents: number = 0;

  constructor(overload: typeof Fee.byFeeText, text: string);
  constructor(overload: typeof Fee.byFeeCents$, value: number);
  constructor(text: string);
  constructor(value: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Fee.byFeeText) {
      const text = args[1] as string;
      this.cents = text.length;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Fee.byFeeCents$) {
      const value = args[1] as number;
      this.cents = value;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const text = args[0] as string;
      this.cents = text.length;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const value = args[0] as number;
      this.cents = value;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromText(text: string): Fee {
    return new Fee(Fee.byFeeText, text);
  }

  static fromCents$(value: number): Fee {
    return new Fee(Fee.byFeeCents$, value);
  }

  static fromCents(value: number): Fee {
    return Fee.fromCents$(value);
  }
}
