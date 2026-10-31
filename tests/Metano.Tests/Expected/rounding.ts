import { MidpointRounding, roundDecimal, roundNumber } from "metano-runtime";
import { Decimal } from "decimal.js";

export class Numbers {
  constructor() { }

  static whole(x: number): number {
    return roundNumber(x);
  }

  static cents(x: number | null, fallback: number): number {
    return roundNumber(x ?? fallback, 2);
  }

  static away(x: number): number {
    return roundNumber(x, 0, MidpointRounding.AwayFromZero);
  }

  static mode(x: number, digits: number, mode: MidpointRounding): number {
    return roundNumber(x, digits, mode);
  }

  static truncated(x: number): number {
    return Math.trunc(x);
  }

  static price(amount: Decimal): Decimal {
    return roundDecimal(amount.times(new Decimal("1.1")), 2);
  }

  static even(amount: Decimal): Decimal {
    return roundDecimal(amount, 0, MidpointRounding.ToEven);
  }
}
