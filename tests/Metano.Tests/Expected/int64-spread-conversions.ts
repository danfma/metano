import { HashSet, List } from "metano-runtime/system/collections";

export class Spreads {
  toList(ints: List<number>): List<bigint> {
    return List.of<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);
  }

  toSet(ints: number[]): HashSet<bigint> {
    return HashSet.of<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);
  }

  toArray(ints: number[]): bigint[] {
    return [...Array.from(ints, (element$temp$0) => BigInt(element$temp$0)), 3n];
  }

  toPairs(pairs: List<[number, number]>): List<[bigint, bigint]> {
    return List.of<[bigint, bigint]>([...Array.from(pairs, (element$temp$0) => [BigInt(element$temp$0[0]), BigInt(element$temp$0[1])] as [bigint, bigint])]);
  }

  same(longs: List<bigint>): List<bigint> {
    return List.of<bigint>([...longs]);
  }
}
