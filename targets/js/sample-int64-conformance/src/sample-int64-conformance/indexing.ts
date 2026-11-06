export function elementAt(values: bigint[], index: bigint): bigint {
  return values[Number(index)];
}

export function intElementAt(values: number[], index: bigint): number {
  return values[Number(index)];
}

export function longLengthOf(values: bigint[]): bigint {
  return BigInt(values.length);
}

export function sizedLongs(length: bigint): bigint[] {
  return new Array<bigint>(Number(length)).fill(0n);
}

export function sizedInts(length: number): number[] {
  return new Array<number>(length).fill(0);
}
