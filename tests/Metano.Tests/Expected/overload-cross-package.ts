import { Money } from "lib/lib";

export class Till extends Money {
  constructor(cents: number) {
    super(Money.byMoneyCentsScale, cents, 2);
  }

  static price(): Money {
    return Money.fromCentsScale(5, 2);
  }
}
