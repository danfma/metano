import { isInt32 } from "metano-runtime";

export class Money {
  constructor() { }

  static __addMoneyInt32(a: Money, b: number): Money {
    return a;
  }

  static __addMoneyUInt32(a: Money, b: number): Money {
    return a;
  }

  static __addMoneyMoney(a: Money, b: Money): Money {
    return a;
  }

  static __add(a: Money, b: number): Money;
  static __add(a: Money, b: number): Money;
  static __add(a: Money, b: Money): Money;
  static __add(...args: unknown[]): Money {
    if (args.length === 2 && args[0] instanceof Money && isInt32(args[1])) {
      return Money.__addMoneyInt32(args[0] as Money, args[1] as number);
    }

    if (args.length === 2 && args[0] instanceof Money && isInt32(args[1])) {
      return Money.__addMoneyUInt32(args[0] as Money, args[1] as number);
    }

    if (args.length === 2 && args[0] instanceof Money && args[1] instanceof Money) {
      return Money.__addMoneyMoney(args[0] as Money, args[1] as Money);
    }

    throw new Error("No matching overload for add");
  }

  $add(b: number): Money;
  $add(b: number): Money;
  $add(b: Money): Money;
  $add(...args: unknown[]): Money {
    if (args.length === 1 && isInt32(args[0])) {
      return Money.__addMoneyInt32(this, args[0] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return Money.__addMoneyUInt32(this, args[0] as number);
    }

    if (args.length === 1 && args[0] instanceof Money) {
      return Money.__addMoneyMoney(this, args[0] as Money);
    }

    throw new Error("No matching overload for add");
  }
}
