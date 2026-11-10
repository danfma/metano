export class Unwrapping {
  constructor() { }

  of(pair: [number, number] | null): [bigint, bigint] {
    return ((receiver$temp$0) => [BigInt(receiver$temp$0[0]), BigInt(receiver$temp$0[1])] as [bigint, bigint])(pair ?? (() => {
      throw new Error("Nullable object must have a value.");
    })());
  }
}
