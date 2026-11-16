import { Money, discount } from "lib/lib";
import { Decimal } from "metano-runtime/system/decimal";

export class Register {
  constructor() { }

  total(): Money {
    return new Money(150).add(25).add(new Money(new Decimal("1")));
  }

  sale(m: Money): Money {
    return discount(discount(m, 10), new Money(5));
  }
}
