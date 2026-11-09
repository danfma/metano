import { checkedNarrow, tupleEquals } from "metano-runtime";

export class Pairs {
  constructor() { }

  returned(pair: [number, number]): [bigint, bigint] {
    return [BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint];
  }

  argument(pair: [number, number]): bigint {
    return this.sum([BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint]);
  }

  sum(pair: [bigint, bigint]): bigint {
    return BigInt.asIntN(64, pair[0] + pair[1]);
  }

  assigned(pair: [number, number]): [bigint, bigint] {
    const wide = [BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint];

    return wide;
  }

  evaluated(next: () => [number, number]): [bigint, bigint] {
    return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])(next());
  }

  nested(pair: [[number, number], string]): [[bigint, bigint], string] {
    return [[BigInt(pair[0][0]), BigInt(pair[0][1])] as [bigint, bigint], pair[1]] as [[bigint, bigint], string];
  }

  nullableElement(pair: [number | null, number]): [bigint | null, bigint] {
    return [pair[0] == null ? null : BigInt(pair[0]), BigInt(pair[1])] as [bigint | null, bigint];
  }

  nullableTuple(pair: [number, number] | null): [bigint, bigint] | null {
    return pair == null ? null : [BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint];
  }

  narrowed(pair: [bigint, bigint]): [number, number] {
    return [Number(BigInt.asIntN(32, pair[0])), Number(BigInt.asIntN(32, pair[1]))] as [number, number];
  }

  narrowedChecked(pair: [bigint, bigint]): [number, number] {
    return [Number(checkedNarrow(pair[0], 32, true)), Number(checkedNarrow(pair[1], 32, true))] as [number, number];
  }

  equalsOperator(wide: [bigint, bigint], narrow: [number, number]): boolean {
    return wide[0] === BigInt(narrow[0]) && wide[1] === BigInt(narrow[1]);
  }

  notEqualsOperator(narrow: [number, number], wide: [bigint, bigint]): boolean {
    return BigInt(narrow[0]) !== wide[0] || BigInt(narrow[1]) !== wide[1];
  }

  equalsLiteral(wide: [bigint, bigint]): boolean {
    return wide[0] === 1n && wide[1] === 2n;
  }

  equalsMethod(wide: [bigint, bigint], narrow: [number, number]): boolean {
    return tupleEquals(wide, [BigInt(narrow[0]), BigInt(narrow[1])] as [bigint, bigint]);
  }

  sameTypes(a: [bigint, bigint], b: [bigint, bigint]): boolean {
    return a[0] === b[0] && a[1] === b[1];
  }
}
