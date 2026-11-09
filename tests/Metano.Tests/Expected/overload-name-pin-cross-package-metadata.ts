import { Decimal } from "decimal.js";
import { Money, discount } from "lib/lib";

export class Register {
  constructor() { }

  total(): Money {
    return new Money(150).add(25).add(new Money(new Decimal("1")));
  }

  sale(m: Money): Money {
    return discount(discount(m, 10), new Money(5));
  }
}
