import { Money } from "./money";

export class Ledger {
  constructor() { }

  static addCents(m: Money): Money {
    return Money.__addMoneyInt32(m, 5);
  }

  static addMoney(a: Money, b: Money): Money {
    return Money.__addMoneyMoney(a, b);
  }
}
