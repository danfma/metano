import { Money } from "./money";

export class Coupon extends Money {
  constructor(cents: number) {
    super(Money.byMoneyCents, cents);
  }

  static fromAmount(text: string): Coupon {
    return new Coupon(text.length);
  }
}
