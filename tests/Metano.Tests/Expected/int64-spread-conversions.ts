import { HashSet } from "metano-runtime";

export class Spreads {
  constructor() { }

  toList(ints: number[]): bigint[] {
    return [...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))];
  }

  toSet(ints: number[]): HashSet<bigint> {
    return new HashSet<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);
  }

  toArray(ints: number[]): bigint[] {
    return [...Array.from(ints, (element$temp$0) => BigInt(element$temp$0)), 3n];
  }

  toPairs(pairs: [number, number][]): [bigint, bigint][] {
    return [...Array.from(pairs, (element$temp$0) => [BigInt(element$temp$0[0]), BigInt(element$temp$0[1])] as [bigint, bigint])];
  }

  same(longs: bigint[]): bigint[] {
    return [...longs];
  }
}
