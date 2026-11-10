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

  iterated(pairs: [number, number][]): bigint {
    let total = 0n;
    for (const pair$element of pairs) {
      const pair = [BigInt(pair$element[0]), BigInt(pair$element[1])] as [bigint, bigint];
      total = BigInt.asIntN(64, total + BigInt.asIntN(64, pair[0] + pair[1]));
    }

    return total;
  }

  iteratedNarrowed(pairs: [bigint, bigint][]): number {
    let total = 0;
    for (const pair$element of pairs) {
      const pair = [Number(BigInt.asIntN(32, pair$element[0])), Number(BigInt.asIntN(32, pair$element[1]))] as [number, number];
      total += pair[0] + pair[1];
    }

    return total;
  }

  iteratedNarrowedChecked(pairs: [bigint, bigint][]): number {
    let total = 0;

    {
      for (const pair$element of pairs) {
        const pair = [Number(checkedNarrow(pair$element[0], 32, true)), Number(checkedNarrow(pair$element[1], 32, true))] as [number, number];
        total += pair[0] + pair[1];
      }
    }

    return total;
  }

  iteratedNullable(pairs: ([number, number] | null)[]): bigint {
    let total = 0n;
    for (const pair$element of pairs) {
      const pair = ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])(pair$element ?? (() => {
        throw new Error("Nullable object must have a value.");
      })());
      total = BigInt.asIntN(64, total + BigInt.asIntN(64, pair[0] + pair[1]));
    }

    return total;
  }

  iteratedLifted(pairs: ([number, number] | null)[]): number {
    let missing = 0;
    for (const pair$element of pairs) {
      const pair = pair$element == null ? null : [BigInt(pair$element[0]), BigInt(pair$element[1])] as [bigint, bigint];
      missing += pair == null ? 1 : 0;
    }

    return missing;
  }

  unwrappedElement(pair: [number | null, number]): [bigint, bigint] {
    return [BigInt(pair[0] ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), BigInt(pair[1])] as [bigint, bigint];
  }

  unwrappedNested(pair: [[number, number] | null, number]): [[bigint, bigint], bigint] {
    return [((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])(pair[0] ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), BigInt(pair[1])] as [[bigint, bigint], bigint];
  }

  liftedCast(pair: [number, number] | null): [bigint, bigint] | null {
    return pair == null ? null : [BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint];
  }

  literalUnwrapped(first: number | null, second: number): [bigint, bigint] {
    return [BigInt(first ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), BigInt(second)] as [bigint, bigint];
  }

  literalNested(first: number | null, second: number): [[bigint, bigint], bigint] {
    return [[BigInt(first ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), BigInt(second)] as [bigint, bigint], BigInt(second)] as [[bigint, bigint], bigint];
  }

  literalNarrowed(first: bigint): [number, number] {
    return [Number(BigInt.asIntN(32, first)), Number(BigInt.asIntN(32, 5n))] as [number, number];
  }

  literalNarrowedChecked(first: bigint): [number, number] {
    return [Number(checkedNarrow(first, 32, true)), Number(checkedNarrow(5n, 32, true))] as [number, number];
  }

  literalHoldingATuple(pair: [number, number], last: number): [[bigint, bigint], bigint] {
    return [[BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint], BigInt(last)] as [[bigint, bigint], bigint];
  }

  castLiteralReturned(a: bigint, b: bigint): [bigint, bigint] {
    return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, b))] as [number, number]);
  }

  castLiteralReturnedNullable(a: bigint, b: bigint): [bigint, bigint] | null {
    return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, b))] as [number, number]);
  }

  castLiteralAssigned(a: bigint, b: bigint): [bigint, bigint] {
    const wide = ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, b))] as [number, number]);

    return wide;
  }

  castLiteralArgument(a: bigint, b: bigint): bigint {
    return this.sum(((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, b))] as [number, number]));
  }

  castLiteralElement(a: bigint, b: bigint): [bigint, bigint][] {
    return [((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, b))] as [number, number])];
  }

  castLiteralArm(narrow: boolean, a: bigint, b: bigint): [bigint, bigint] {
    return narrow ? ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, b))] as [number, number]) : [a, b] as [bigint, bigint];
  }

  castLiteralNested(a: bigint, b: bigint): [[bigint, bigint], number] {
    return [((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, b))] as [number, number]), 3] as [[bigint, bigint], number];
  }

  castTupleAssigned(pair: [bigint, bigint]): [bigint, bigint] {
    const wide = ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])([Number(BigInt.asIntN(32, pair[0])), Number(BigInt.asIntN(32, pair[1]))] as [number, number]);

    return wide;
  }

  nullableLiteral(): [bigint, bigint] | null {
    return [1n, 2n] as [bigint, bigint];
  }

  forgiven(pair: [number, number] | null): [bigint, bigint] | null {
    return pair == null ? null : [BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint];
  }
}
