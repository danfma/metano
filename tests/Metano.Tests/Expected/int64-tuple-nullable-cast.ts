export class Unwrapping {
  constructor() { }

  of(pair: [number, number] | null): [bigint, bigint] {
    return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])(pair ?? (() => {
      throw new Error("Nullable object must have a value.");
    })());
  }

  ofKnownPresent(): [bigint, bigint] {
    return [1n, BigInt(2)] as [bigint, bigint];
  }

  ofChecked(first: number | null, second: number): [bigint, bigint] {
    return first == null ? [0n, 0n] as [bigint, bigint] : [BigInt(first), BigInt(second)] as [bigint, bigint];
  }

  ofDefault(first: number | null): [bigint, bigint | null] {
    return [BigInt(first ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), null] as [bigint, bigint | null];
  }

  ofNull(first: number | null, second: number): [bigint, [bigint, string | null]] {
    return [BigInt(first ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()), [BigInt(second), null] as [bigint, string | null]] as [bigint, [bigint, string | null]];
  }
}
