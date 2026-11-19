import { List } from "metano-runtime/system/collections";
import { isInt32 } from "metano-runtime";
import { Decimal } from "#/other/decimal";
import type { A } from "./a";
import type { B } from "./b";

export class Climbs {
  constructor() { }

  kNumericsDecimal(d: Decimal): number {
    return 1;
  }

  kOtherDecimal(d: Decimal): number {
    return 2;
  }

  k(d: Decimal): number;
  k(d: Decimal): number;
  k(...args: unknown[]): number {
    if (args.length === 1 && args[0] instanceof Decimal) {
      return this.kNumericsDecimal(args[0] as Decimal);
    }

    if (args.length === 1 && args[0] instanceof Decimal) {
      return this.kOtherDecimal(args[0] as Decimal);
    }

    throw new Error("No matching overload for k");
  }

  jNumericsDecimalInt32(d: Decimal, n: number): number {
    return 1;
  }

  jOtherDecimalInt32(d: Decimal, n: number): number {
    return 2;
  }

  j(d: Decimal, n: number): number;
  j(d: Decimal, n: number): number;
  j(...args: unknown[]): number {
    if (args.length === 2 && args[0] instanceof Decimal && isInt32(args[1])) {
      return this.jNumericsDecimalInt32(args[0] as Decimal, args[1] as number);
    }

    if (args.length === 2 && args[0] instanceof Decimal && isInt32(args[1])) {
      return this.jOtherDecimalInt32(args[0] as Decimal, args[1] as number);
    }

    throw new Error("No matching overload for j");
  }

  mListOfAId(ids: List<A.Id>): number {
    return 1;
  }

  mListOfBId(ids: List<B.Id>): number {
    return 2;
  }

  m(ids: List<A.Id>): number;
  m(ids: List<B.Id>): number;
  m(...args: unknown[]): number {
    if (args.length === 1 && args[0] instanceof List) {
      return this.mListOfAId(args[0] as List<A.Id>);
    }

    if (args.length === 1 && args[0] instanceof List) {
      return this.mListOfBId(args[0] as List<B.Id>);
    }

    throw new Error("No matching overload for m");
  }
}
