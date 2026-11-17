export class Money {
  cents: number = 0;

  constructor() { }

  static __toInt(m: Money): number {
    return m.cents;
  }

  static __toLong(m: Money): bigint {
    return BigInt(m.cents);
  }

  static __negate(m: Money): Money {
    return m;
  }

  $negate(): Money {
    return Money.__negate(this);
  }
}
