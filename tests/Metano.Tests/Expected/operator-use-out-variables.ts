import { Money } from "./money.js";

export class Uses {
  private constructor() { }

  private static try(): readonly [found: boolean, value: number] {
    let value: number;
    value = 4;

    return [true, value];
  }

  static convert(): Money {
    const [$found0, k] = Uses.try();
    const m = Money.__implicitFromInt32($found0 ? k : 0);

    return m;
  }

  static add(b: Money): Money {
    const [$found0, q] = Uses.try();
    const s = Money.__add(Money.__implicitFromInt32($found0 ? q : 1), b);

    return s;
  }

  static store(m: Money): Money {
    const [$found0, r] = Uses.try();
    m = Money.__add(m, Money.__implicitFromInt32($found0 ? r : 2));

    return m;
  }
}
