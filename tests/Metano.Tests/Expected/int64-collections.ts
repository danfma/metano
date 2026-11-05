import { averageInt64, averageNullableInt64, count, linq, maxInt64Of, maxNullableInt64Of, minInt64Of, minUInt64Of, sum, sumInt64 } from "metano-runtime/system/linq";
import type { Item } from "./item";

export class Collections {
  private _cursor: number = 0;

  slots: bigint[] = new Array<bigint>(4).fill(0n);

  constructor() { }

  next(): number {
    return this._cursor++;
  }

  index(values: number[], l: bigint, u: bigint): number {
    return values[Number(l)] + values[Number(u)];
  }

  write(values: bigint[], l: bigint): void {
    values[Number(l)] = 1n;
    values[Number(l)] = BigInt.asIntN(64, values[Number(l)] + 2n);
    ((receiver$temp$0) => values[receiver$temp$0] = BigInt.asIntN(64, values[receiver$temp$0] * 3n))(this.next());
    this.slots[Number(l)] = BigInt.asIntN(64, this.slots[Number(l)] + 1n);
  }

  length(values: number[]): bigint {
    return BigInt(values.length);
  }

  zeros(n: number): bigint[] {
    return new Array<bigint>(n).fill(0n);
  }

  byLongLength(n: bigint): number[] {
    return new Array<number>(Number(n)).fill(0);
  }

  strings(n: number): string[] {
    return new Array(n).fill(null);
  }

  flags(n: number): boolean[] {
    return new Array<boolean>(n).fill(false);
  }

  items(n: number): Item[] {
    return new Array(n).fill(null);
  }

  count(xs: number[]): bigint {
    return BigInt.asIntN(64, BigInt(linq(xs, count())) + BigInt(linq(xs, count((x: number) => x > 1))));
  }

  intCount(xs: number[]): number {
    return linq(xs, count());
  }

  sum(ids: bigint[], items: Item[]): bigint {
    return BigInt.asIntN(64, linq(ids, sumInt64()) + linq(items, sumInt64((x: Item) => x.size)));
  }

  nullableSum(items: Item[]): bigint | null {
    return linq(items, sumInt64((x: Item) => x.optional));
  }

  average(ids: bigint[], optional: (bigint | null)[], items: Item[]): number {
    return linq(ids, averageInt64()) + (linq(optional, averageNullableInt64()) ?? 0) + linq(items, averageInt64((x: Item) => x.size));
  }

  extremes(ids: bigint[], items: Item[], flags: bigint[]): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, BigInt.asIntN(64, linq(ids, maxInt64Of()) + linq(items, minInt64Of((x: Item) => x.size))) + (linq(items, maxNullableInt64Of((x: Item) => x.optional)) ?? 0n)) + BigInt.asIntN(64, linq(flags, minUInt64Of())));
  }

  intSum(xs: number[]): number {
    return linq(xs, sum());
  }
}
