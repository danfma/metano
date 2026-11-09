import { Decimal } from "decimal.js";
import { Money, discount } from "lib/lib";

export class Register {
  constructor() { }

  total(): Money {
    return Money.fromCents(150).addCents(25).addOther(Money.fromAmount(new Decimal("1")));
  }

  sale(m: Money): Money {
    return discount(discount(m, 10), Money.fromCents(5));
  }
}
