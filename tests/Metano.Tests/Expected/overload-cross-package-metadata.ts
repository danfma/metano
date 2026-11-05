import { Money } from "lib/lib";

export class Till extends Money {
  constructor(cents: number) {
    super(cents);
  }

  static price(): Money {
    return new Money(5);
  }
}
