import { isInt32, isString } from "metano-runtime";

export class Money {
  protected static readonly byMoneyText: unique symbol = Symbol("Money(string text)");

  protected static readonly byMoneyCents: unique symbol = Symbol("Money(int value)");

  constructor(overload: typeof Money.byMoneyText, text: string);
  constructor(overload: typeof Money.byMoneyCents, value: number);
  constructor(text: string);
  constructor(value: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyText) {
      const text = args[1] as string;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyCents) {
      const value = args[1] as number;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const text = args[0] as string;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const value = args[0] as number;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromText(text: string): Money {
    return new Money(Money.byMoneyText, text);
  }

  static fromCents(value: number): Money {
    return new Money(Money.byMoneyCents, value);
  }

  addInt32(x: number): number {
    return x;
  }

  addX(s: string): number {
    return 0;
  }

  add(x: number): number;
  add(s: string): number;
  add(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addInt32(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.addX(args[0] as string);
    }

    throw new Error("No matching overload for add");
  }
}
