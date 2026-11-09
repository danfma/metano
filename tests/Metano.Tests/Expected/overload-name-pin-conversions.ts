export class Money {
  cents: number = 0;

  constructor() { }

  static __toIntMoneyToInt32(m: Money): number {
    return m.cents;
  }

  static __toIntMoneyToInt64(m: Money): bigint {
    return BigInt(m.cents);
  }

  static __toInt(m: Money): number;
  static __toInt(m: Money): bigint;
  static __toInt(...args: unknown[]): number {
    if (args.length === 1 && args[0] instanceof Money) {
      return Money.__toIntMoneyToInt32(args[0] as Money);
    }

    if (args.length === 1 && args[0] instanceof Money) {
      return Money.__toIntMoneyToInt64(args[0] as Money);
    }

    throw new Error("No matching overload for toInt");
  }

  $toInt(): number;
  $toInt(): bigint;
  $toInt(...args: unknown[]): number {
    if (args.length === 0) {
      return Money.__toIntMoneyToInt32(this);
    }

    if (args.length === 0) {
      return Money.__toIntMoneyToInt64(this);
    }

    throw new Error("No matching overload for toInt");
  }

  static __negate(m: Money): Money {
    return m;
  }

  $negate(): Money {
    return Money.__negate(this);
  }
}
