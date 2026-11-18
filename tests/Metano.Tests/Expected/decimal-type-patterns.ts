import { isDecimal } from "metano-runtime";
import { Decimal } from "metano-runtime/system/decimal";

export class Checker {
  constructor() { }

  static isDecimal(o: Object): boolean {
    return isDecimal(o);
  }

  static isNotDecimal(o: Object): boolean {
    return !isDecimal(o);
  }

  static isPositive(o: Object): boolean {
    let d: Decimal;

    return isDecimal(o) && (d = o, true) && d.gt(new Decimal("0"));
  }
}
