import { List } from "metano-runtime/system/collections";
import type { A } from "./a.js";
import type { B } from "./b.js";
import type { Decimal } from "./numerics/decimal.js";

export class Climbs {
  kNumericsDecimal(d: Decimal): number {
    return 1;
  }

  kOtherDecimal(d: Decimal): number {
    return 2;
  }

  k(d: Decimal): number {
    return this.kNumericsDecimal(d);
  }

  jNumericsDecimalInt32(d: Decimal, n: number): number {
    return 1;
  }

  jOtherDecimalInt32(d: Decimal, n: number): number {
    return 2;
  }

  j(d: Decimal, n: number): number {
    return this.jNumericsDecimalInt32(d, n);
  }

  mListOfAId(ids: List<A.Id>): number {
    return 1;
  }

  mListOfBId(ids: List<B.Id>): number {
    return 2;
  }

  m(ids: List<A.Id>): number {
    return this.mListOfAId(ids);
  }
}
