import { checkedNarrow, throwError, toException, tupleEquals } from "metano-runtime";
import { HashSet, List, throwModified } from "metano-runtime/system/collections";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

export function equalsOperator(a: number, b: number): boolean {
  const wide = [BigInt(a), BigInt(b)] as [bigint, bigint];
  const narrow = [a, b] as [number, number];

  return wide[0] === BigInt(narrow[0]) && wide[1] === BigInt(narrow[1]) && BigInt(narrow[0]) === wide[0] && BigInt(narrow[1]) === wide[1];
}

export function notEqualsOperator(a: number, b: number): boolean {
  const wide = [BigInt(a), BigInt(b)] as [bigint, bigint];
  const narrow = [a, b] as [number, number];

  return wide[0] !== BigInt(narrow[0]) || wide[1] !== BigInt(narrow[1]) || BigInt(narrow[0]) !== wide[0] || BigInt(narrow[1]) !== wide[1];
}

export function equalsLiteral(a: number, b: number): boolean {
  const wide = [BigInt(a), BigInt(b)] as [bigint, bigint];

  return wide[0] === BigInt(a) && wide[1] === BigInt(b);
}

export function equalsMethod(a: number, b: number): boolean {
  const wide = [BigInt(a), BigInt(b)] as [bigint, bigint];
  const narrow = [a, b] as [number, number];

  return tupleEquals(wide, [BigInt(narrow[0]), BigInt(narrow[1])] as [bigint, bigint]);
}

export function assignedSum(a: number, b: number): bigint {
  const narrow = [a, b] as [number, number];
  const wide = [BigInt(narrow[0]), BigInt(narrow[1])] as [bigint, bigint];

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function returnedSum(a: number, b: number): bigint {
  const widen = (pair: [number, number]) => [BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint];
  const narrow = [a, b] as [number, number];
  const wide = widen(narrow);

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function argumentSum(a: number, b: number): bigint {
  const sum = (pair: [bigint, bigint]) => BigInt.asIntN(64, pair[0] + pair[1]);
  const narrow = [a, b] as [number, number];

  return sum([BigInt(narrow[0]), BigInt(narrow[1])] as [bigint, bigint]);
}

export function nestedSum(a: number, b: number): bigint {
  const narrow = [[a, b] as [number, number], a] as [[number, number], number];
  const wide = [[BigInt(narrow[0][0]), BigInt(narrow[0][1])] as [bigint, bigint], BigInt(narrow[1])] as [[bigint, bigint], bigint];

  return BigInt.asIntN(64, BigInt.asIntN(64, wide[0][0] + wide[0][1]) + wide[1]);
}

export function namedSum(a: number, b: number): bigint {
  const narrow = [a, b] as [number, number];
  const wide = [BigInt(narrow[0]), BigInt(narrow[1])] as [bigint, bigint];

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function mixedText(a: number, b: number): string {
  const narrow = [a, b.toString()] as [number, string];
  const wide = [BigInt(narrow[0]), narrow[1]] as [bigint, string];

  return BigInt.asIntN(64, wide[0] + 1n) + (wide[1] ?? "");
}

export function nullableElementSum(a: number, b: number): bigint {
  const narrow = [a, b] as [number | null, number];
  const wide = [narrow[0] == null ? null : BigInt(narrow[0]), BigInt(narrow[1])] as [bigint | null, bigint];

  return BigInt.asIntN(64, (wide[0] ?? 0n) + wide[1]);
}

export function nullableElementStaysNull(b: number): boolean {
  const narrow = [null, b] as [number | null, number];
  const wide = [narrow[0] == null ? null : BigInt(narrow[0]), BigInt(narrow[1])] as [bigint | null, bigint];

  return wide[0] == null && wide[1] === BigInt(b);
}

export function nullableTupleSum(a: number, b: number): bigint {
  const narrow = [a, b] as [number, number];
  const wide = narrow == null ? null : [BigInt(narrow[0]), BigInt(narrow[1])] as [bigint, bigint];
  let pair: [bigint, bigint];

  return wide != null && (pair = wide, true) ? BigInt.asIntN(64, pair[0] + pair[1]) : -1n;
}

export function nullableTupleStaysNull(): boolean {
  const narrow = null as [number, number] | null;
  const wide = narrow == null ? null : [BigInt(narrow[0]), BigInt(narrow[1])] as [bigint, bigint];

  return wide == null;
}

export function explicitNarrowed(a: bigint): number {
  const wide = [a, a] as [bigint, bigint];
  const narrow = [Number(BigInt.asIntN(32, wide[0])), Number(BigInt.asIntN(32, wide[1]))] as [number, number];

  return narrow[0];
}

export function iteratedSum(a: number, b: number): bigint {
  const pairs = List.of<[number, number]>([[a, b] as [number, number], [b, a] as [number, number]]);
  let total = 0n;
  {
    const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
    const $l = pairs;
    const $a = $l._items;
    const $n = $a.length;
    const $v = $c ? $l._version : 0;
    for (let $i = 0; ; $i++) {
      if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
      if ($i === $n) break;
      const pair$element = $a[$i]!;
      const pair = [BigInt(pair$element[0]), BigInt(pair$element[1])] as [bigint, bigint];
      total = BigInt.asIntN(64, total + BigInt.asIntN(64, BigInt.asIntN(64, pair[0] - pair[1]) + pair[0]));
    }
  }

  return total;
}

export function nullableIteratedStop(a: number, b: number): bigint {
  const pairs = List.of<[number, number] | null>([[a, b] as [number, number], null, [b, a] as [number, number]]);
  let count = 0;
  let total = 0n;
  try {
    {
      const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
      const $l = pairs;
      const $a = $l._items;
      const $n = $a.length;
      const $v = $c ? $l._version : 0;
      for (let $i = 0; ; $i++) {
        if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
        if ($i === $n) break;
        const pair$element = $a[$i]!;
        const receiver$temp$0 = pair$element ?? throwError(new InvalidOperationException("Nullable object must have a value."));
        const pair = [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint];
        count = (count + 1) | 0;
        total = BigInt.asIntN(64, total + pair[0]);
      }
    }
  } catch (e$) {
    const e0 = toException(e$);
    if (e0 instanceof InvalidOperationException) {
      return BigInt.asIntN(64, total - BigInt(count));
    } else {
      throw e$;
    }
  }

  return total;
}

export function iteratedNarrowedSum(a: bigint): number {
  const pairs = List.of<[bigint, bigint]>([[a, a] as [bigint, bigint]]);
  let total = 0;
  {
    const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
    const $l = pairs;
    const $a = $l._items;
    const $n = $a.length;
    const $v = $c ? $l._version : 0;
    for (let $i = 0; ; $i++) {
      if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
      if ($i === $n) break;
      const pair$element = $a[$i]!;
      const pair = [Number(BigInt.asIntN(32, pair$element[0])), Number(BigInt.asIntN(32, pair$element[1]))] as [number, number];
      total ^= pair[0] ^ pair[1] >> 1;
    }
  }

  return total;
}

export function iteratedNarrowedCheckedSum(a: bigint): number {
  {
    const pairs = List.of<[bigint, bigint]>([[a, a] as [bigint, bigint]]);
    let total = 0;
    {
      const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
      const $l = pairs;
      const $a = $l._items;
      const $n = $a.length;
      const $v = $c ? $l._version : 0;
      for (let $i = 0; ; $i++) {
        if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
        if ($i === $n) break;
        const pair$element = $a[$i]!;
        const pair = [Number(checkedNarrow(pair$element[0], 32, true)), Number(checkedNarrow(pair$element[1], 32, true))] as [number, number];
        total ^= pair[0] ^ pair[1] >> 1;
      }
    }

    return total;
  }
}

export function literalCastStopsAtTheMissingElement(a: number): number {
  let calls = 0;
  const missing = () => {
    calls = (calls + 1) | 0;

    return null;
  };
  const next = () => {
    calls = (calls + 100) | 0;

    return a;
  };
  try {
    const wide = [BigInt(missing() ?? throwError(new InvalidOperationException("Nullable object must have a value."))), BigInt(next())] as [bigint, bigint];

    return Number(BigInt.asIntN(32, BigInt.asIntN(64, wide[0] + wide[1])));
  } catch (e$) {
    const e0 = toException(e$);
    if (e0 instanceof InvalidOperationException) {
      return -calls | 0;
    } else {
      throw e$;
    }
  }
}

export function literalCastSum(a: number, b: number): bigint {
  const first = a;
  const wide = [[BigInt(first ?? throwError(new InvalidOperationException("Nullable object must have a value."))), BigInt(b)] as [bigint, bigint], BigInt(a)] as [[bigint, bigint], bigint];

  return BigInt.asIntN(64, BigInt.asIntN(64, wide[0][0] + wide[0][1]) + wide[1]);
}

export function literalCastNarrowed(a: bigint): number {
  const narrow = [Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, 5n))] as [number, number];

  return narrow[0] ^ narrow[1] << 4;
}

export function literalCastNarrowedChecked(a: bigint): number {
  const narrow = [Number(checkedNarrow(a, 32, true)), Number(checkedNarrow(5n, 32, true))] as [number, number];

  return narrow[0] ^ narrow[1] << 4;
}

export function literalCastKeepsTheMissingTuple(a: number): bigint {
  let receiver$temp$0;
  const missing = () => null;
  const wide = [(receiver$temp$0 = missing(), receiver$temp$0 == null ? null : [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint]), BigInt(a)] as [[bigint, bigint] | null, bigint];

  return wide[0] == null ? wide[1] : -1n;
}

export function literalCastThrowsOnTheMissingLong(a: bigint): number {
  const missing = () => null;
  try {
    const wide = [Number(a), Number(missing() ?? throwError(new InvalidOperationException("Nullable object must have a value.")))] as [number, number];

    return wide[1] > 0 ? 1 : 2;
  } catch (e$) {
    const e0 = toException(e$);
    if (e0 instanceof InvalidOperationException) {
      return -1;
    } else {
      throw e$;
    }
  }
}

export function literalCastOfWrappedValues(a: number): bigint {
  const wide = [BigInt(2), BigInt(a)] as [bigint, bigint];

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function literalCastWithDefault(a: number): bigint {
  const first = a;
  const wide = [BigInt(first ?? throwError(new InvalidOperationException("Nullable object must have a value."))), null] as [bigint, bigint | null];

  return BigInt.asIntN(64, wide[0] + (wide[1] ?? 1n));
}

export function castLiteralInitializedSum(a: bigint): bigint {
  const receiver$temp$0 = [Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, a))] as [number, number];
  const wide = [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint];

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function castLiteralArgumentSum(a: bigint): bigint {
  let receiver$temp$0;
  const sum = (pair: [bigint, bigint]) => BigInt.asIntN(64, pair[0] + pair[1]);

  return sum((receiver$temp$0 = [Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, a))] as [number, number], [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint]));
}

export function castLiteralReturnedSum(a: bigint): bigint {
  const narrowed = () => {
    const receiver$temp$0 = [Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, a))] as [number, number];

    return [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint];
  };
  const wide = narrowed();

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function castLiteralNullableSum(a: bigint): bigint {
  const receiver$temp$0 = [Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, a))] as [number, number];
  const wide = [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint];
  let pair: [bigint, bigint];

  return wide != null && (pair = wide, true) ? BigInt.asIntN(64, pair[0] + pair[1]) : -1n;
}

export function castLiteralElementSum(a: bigint): bigint {
  let receiver$temp$0;
  const pairs = List.of<[bigint, bigint]>([(receiver$temp$0 = [Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, a))] as [number, number], [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])]);

  return BigInt.asIntN(64, pairs.get(0)[0] + pairs.get(0)[1]);
}

export function castLiteralConditionalSum(a: bigint): bigint {
  let receiver$temp$0;
  const wide = a > 0n ? (receiver$temp$0 = [Number(BigInt.asIntN(32, a)), Number(BigInt.asIntN(32, a))] as [number, number], [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint]) : [5n, 6n] as [bigint, bigint];

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function castLocalTupleSum(a: bigint): bigint {
  const source = [a, a] as [bigint, bigint];
  const receiver$temp$0 = [Number(BigInt.asIntN(32, source[0])), Number(BigInt.asIntN(32, source[1]))] as [number, number];
  const wide = [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint];

  return BigInt.asIntN(64, wide[0] + wide[1]);
}

export function nullableLiteralSum(a: number, b: number): bigint {
  const wide = [BigInt(a), BigInt(b)] as [bigint, bigint];
  let pair: [bigint, bigint];

  return wide != null && (pair = wide, true) ? BigInt.asIntN(64, pair[0] + pair[1]) : -1n;
}

export function spreadListSum(a: number, b: number): bigint {
  const ints = List.of<number>([a, b]);
  const longs = List.of<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);

  return BigInt.asIntN(64, longs.get(0) + longs.get(1));
}

export function spreadListContains(a: number, b: number): boolean {
  const ints = List.of<number>([a, b]);
  const longs = List.of<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);

  return longs.contains(BigInt(a));
}

export function spreadSetContains(a: number, b: number): boolean {
  const ints = [a, b];
  const longs = HashSet.of<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);

  return longs.contains(BigInt(b));
}

export function spreadArraySum(a: number, b: number): bigint {
  const ints = [a, b];
  const longs = [...Array.from(ints, (element$temp$0) => BigInt(element$temp$0)), 3n];

  return BigInt.asIntN(64, BigInt.asIntN(64, longs[0]! + longs[1]!) + longs[2]!);
}

export function spreadTupleSum(a: number, b: number): bigint {
  const pairs = List.of<[number, number]>([[a, b] as [number, number]]);
  const wide = List.of<[bigint, bigint]>([...Array.from(pairs, (element$temp$0) => [BigInt(element$temp$0[0]), BigInt(element$temp$0[1])] as [bigint, bigint])]);

  return BigInt.asIntN(64, wide.get(0)[0] + wide.get(0)[1]);
}
