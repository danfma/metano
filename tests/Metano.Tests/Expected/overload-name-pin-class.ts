import { isBool, isInt32, isString } from "metano-runtime";

export class Money {
  protected static readonly byMoneyText: unique symbol = Symbol("Money(string text)");

  protected static readonly byMoneyCents: unique symbol = Symbol("Money(int value)");

  readonly cents: number = 0;

  constructor(overload: typeof Money.byMoneyText, text: string);
  constructor(overload: typeof Money.byMoneyCents, value: number);
  constructor(text: string);
  constructor(value: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyText) {
      const text = args[1] as string;
      this.cents = text.length;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Money.byMoneyCents) {
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

  static fromText(text: string): Money {
    return new Money(Money.byMoneyText, text);
  }

  static fromCents(value: number): Money {
    return new Money(Money.byMoneyCents, value);
  }

  static __multiplyScaled(a: Money, b: number): Money {
    return Money.fromCents(a.cents * b);
  }

  static __multiplyMoneyString(a: Money, b: string): Money {
    return Money.fromText(b);
  }

  static __multiply(a: Money, b: number): Money;
  static __multiply(a: Money, b: string): Money;
  static __multiply(...args: unknown[]): Money {
    if (args.length === 2 && args[0] instanceof Money && isInt32(args[1])) {
      return Money.__multiplyScaled(args[0] as Money, args[1] as number);
    }

    if (args.length === 2 && args[0] instanceof Money && isString(args[1])) {
      return Money.__multiplyMoneyString(args[0] as Money, args[1] as string);
    }

    throw new Error("No matching overload for multiply");
  }

  $multiply(b: number): Money;
  $multiply(b: string): Money;
  $multiply(...args: unknown[]): Money {
    if (args.length === 1 && isInt32(args[0])) {
      return Money.__multiplyScaled(this, args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return Money.__multiplyMoneyString(this, args[0] as string);
    }

    throw new Error("No matching overload for multiply");
  }

  addOther(other: Money): Money {
    return Money.fromCents(this.cents + other.cents);
  }

  addCents(value: number): Money {
    return this.addOther(Money.fromCents(value));
  }

  add(other: Money): Money;
  add(value: number): Money;
  add(...args: unknown[]): Money {
    if (args.length === 1 && args[0] instanceof Money) {
      return this.addOther(args[0] as Money);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.addCents(args[0] as number);
    }

    throw new Error("No matching overload for add");
  }

  tagInt32(x: number): string {
    return "int";
  }

  tagX(s: string): string {
    return s;
  }

  tagFlag(b: boolean): string {
    return "bool";
  }

  tag(x: number): string;
  tag(s: string): string;
  tag(b: boolean): string;
  tag(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.tagInt32(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.tagX(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.tagFlag(args[0] as boolean);
    }

    throw new Error("No matching overload for tag");
  }

  plus(other: Money): Money {
    return this.addOther(other);
  }

  grow(): Money {
    return Money.__multiplyScaled(this.addCents(1), 2);
  }
}
