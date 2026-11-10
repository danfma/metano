export class Unwrapping {
  constructor() { }

  of(pair: [number, number] | null): [bigint, bigint] {
    return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])(pair ?? (() => {
      throw new Error("Nullable object must have a value.");
    })());
  }

  ofWrapped(second: number): [bigint, bigint] {
    return [BigInt(2), BigInt(second)] as [bigint, bigint];
  }

  ofWidenedWrap(second: number): [bigint, bigint] {
    return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), receiver$temp$0[1]] as [bigint, bigint])([1, BigInt(second)] as [number, bigint]);
  }

  ofChecked(first: number | null, second: number): [bigint, bigint] {
    return first == null ? [0n, 0n] as [bigint, bigint] : [BigInt(first ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), BigInt(second)] as [bigint, bigint];
  }

  ofForgiven(first: number | null, second: number): [bigint, bigint] {
    return [BigInt(first ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), BigInt(second)] as [bigint, bigint];
  }

  async ofAwaited(pending: Promise<bigint | null>, first: bigint): Promise<[number, number]> {
    const second = await pending;

    return [Number(first), Number(second ?? (() => {
      throw new Error("Nullable object must have a value.");
    })())] as [number, number];
  }

  async ofAwaitedTuple(pending: Promise<[number, number] | null>, last: number): Promise<[[bigint, bigint] | null, bigint]> {
    const pair = await pending;

    return [pair == null ? null : [BigInt(pair[0]), BigInt(pair[1])] as [bigint, bigint], BigInt(last)] as [[bigint, bigint] | null, bigint];
  }
}
