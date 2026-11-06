import { Decimal } from "decimal.js";

export class Checker {
  constructor() { }

  static isDecimal(o: Object): boolean {
    return o instanceof Decimal;
  }

  static isNotDecimal(o: Object): boolean {
    return !(o instanceof Decimal);
  }

  static isPositive(o: Object): boolean {
    let d: Decimal;

    return o instanceof Decimal && (d = o, true) && d.gt(new Decimal("0"));
  }
}
