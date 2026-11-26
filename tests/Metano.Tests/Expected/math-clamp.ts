import { clampDecimal, clampInt64, clampNumber } from "metano-runtime";
import { Decimal } from "metano-runtime/system/decimal";

export class Clamps {
  int(v: number): number {
    return clampNumber(v, 0, 10);
  }

  double(v: number): number {
    return clampNumber(v, 0.5, 1.5);
  }

  single(v: number): number {
    return clampNumber(v, 0, 1);
  }

  byte(v: number): number {
    return clampNumber(v, 1, 9);
  }

  long(v: bigint): bigint {
    return clampInt64(v, 0n, 10n);
  }

  decimal(v: Decimal): Decimal {
    return clampDecimal(v, new Decimal("0"), new Decimal("1"));
  }

  staticInt(v: number): number {
    return clampNumber(v, 0, 10);
  }

  staticDouble(v: number): number {
    return clampNumber(v, 0.5, 1.5);
  }

  staticDecimal(v: Decimal): Decimal {
    return clampDecimal(v, new Decimal("0"), new Decimal("1"));
  }
}
