import { HashSet, checkedNarrow, tupleEquals } from "metano-runtime";

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

  return BigInt.asIntN(64, wide[0] + 1n) + wide[1];
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
  const pairs = ((receiver$temp$0) => {
    receiver$temp$0.push([a, b] as [number, number]);
    receiver$temp$0.push([b, a] as [number, number]);

    return receiver$temp$0;
  })([] as Array<[number, number]>);
  let total = 0n;
  for (const pair$element of pairs) {
    const pair = [BigInt(pair$element[0]), BigInt(pair$element[1])] as [bigint, bigint];
    total = BigInt.asIntN(64, total + BigInt.asIntN(64, BigInt.asIntN(64, pair[0] - pair[1]) + pair[0]));
  }

  return total;
}

export function nullableIteratedStop(a: number, b: number): bigint {
  const pairs = ((receiver$temp$0) => {
    receiver$temp$0.push([a, b] as [number, number]);
    receiver$temp$0.push(null);
    receiver$temp$0.push([b, a] as [number, number]);

    return receiver$temp$0;
  })([] as Array<[number, number] | null>);
  let count = 0;
  let total = 0n;
  try {
    for (const pair$element of pairs) {
      const pair = ((receiver$temp$1) => [BigInt(receiver$temp$1[0]), BigInt(receiver$temp$1[1])] as [bigint, bigint])(pair$element ?? (() => {
        throw new Error("Nullable object must have a value.");
      })());
      count++;
      total = BigInt.asIntN(64, total + pair[0]);
    }
  } catch (_err) {
    return BigInt.asIntN(64, total - BigInt(count));
  }

  return total;
}

export function iteratedNarrowedSum(a: bigint): number {
  const pairs = ((receiver$temp$0) => {
    receiver$temp$0.push([a, a] as [bigint, bigint]);

    return receiver$temp$0;
  })([] as Array<[bigint, bigint]>);
  let total = 0;
  for (const pair$element of pairs) {
    const pair = [Number(BigInt.asIntN(32, pair$element[0])), Number(BigInt.asIntN(32, pair$element[1]))] as [number, number];
    total ^= pair[0] ^ pair[1] >> 1;
  }

  return total;
}

export function iteratedNarrowedCheckedSum(a: bigint): number {
  {
    const pairs = ((receiver$temp$0) => {
      receiver$temp$0.push([a, a] as [bigint, bigint]);

      return receiver$temp$0;
    })([] as Array<[bigint, bigint]>);
    let total = 0;
    for (const pair$element of pairs) {
      const pair = [Number(checkedNarrow(pair$element[0], 32, true)), Number(checkedNarrow(pair$element[1], 32, true))] as [number, number];
      total ^= pair[0] ^ pair[1] >> 1;
    }

    return total;
  }
}

export function literalCastStopsAtTheMissingElement(a: number): number {
  let calls = 0;
  const missing = () => {
    calls++;

    return null;
  };
  const next = () => {
    calls += 100;

    return a;
  };
  try {
    const wide = [BigInt(missing() ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), BigInt(next())] as [bigint, bigint];

    return Number(BigInt.asIntN(32, BigInt.asIntN(64, wide[0] + wide[1])));
  } catch (_err) {
    return -calls;
  }
}

export function literalCastSum(a: number, b: number): bigint {
  const first = a;
  const wide = [[BigInt(first ?? (() => {
    throw new Error("Nullable object must have a value.");
  })()), BigInt(b)] as [bigint, bigint], BigInt(a)] as [[bigint, bigint], bigint];

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

export function nullableLiteralSum(a: number, b: number): bigint {
  const wide = [BigInt(a), BigInt(b)] as [bigint, bigint];
  let pair: [bigint, bigint];

  return wide != null && (pair = wide, true) ? BigInt.asIntN(64, pair[0] + pair[1]) : -1n;
}

export function spreadListSum(a: number, b: number): bigint {
  const ints = ((receiver$temp$0) => {
    receiver$temp$0.push(a);
    receiver$temp$0.push(b);

    return receiver$temp$0;
  })([] as Array<number>);
  const longs = [...Array.from(ints, (element$temp$1) => BigInt(element$temp$1))];

  return BigInt.asIntN(64, longs[0] + longs[1]);
}

export function spreadListContains(a: number, b: number): boolean {
  const ints = ((receiver$temp$0) => {
    receiver$temp$0.push(a);
    receiver$temp$0.push(b);

    return receiver$temp$0;
  })([] as Array<number>);
  const longs = [...Array.from(ints, (element$temp$1) => BigInt(element$temp$1))];

  return longs.includes(BigInt(a));
}

export function spreadSetContains(a: number, b: number): boolean {
  const ints = [a, b];
  const longs = new HashSet<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);

  return longs.has(BigInt(b));
}

export function spreadArraySum(a: number, b: number): bigint {
  const ints = [a, b];
  const longs = [...Array.from(ints, (element$temp$0) => BigInt(element$temp$0)), 3n];

  return BigInt.asIntN(64, BigInt.asIntN(64, longs[0] + longs[1]) + longs[2]);
}

export function spreadTupleSum(a: number, b: number): bigint {
  const pairs = ((receiver$temp$0) => {
    receiver$temp$0.push([a, b] as [number, number]);

    return receiver$temp$0;
  })([] as Array<[number, number]>);
  const wide = [...Array.from(pairs, (element$temp$1) => [BigInt(element$temp$1[0]), BigInt(element$temp$1[1])] as [bigint, bigint])];

  return BigInt.asIntN(64, wide[0][0] + wide[0][1]);
}
