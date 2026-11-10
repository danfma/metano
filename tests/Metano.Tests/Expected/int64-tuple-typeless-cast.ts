export class Typeless {
  constructor() { }

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
