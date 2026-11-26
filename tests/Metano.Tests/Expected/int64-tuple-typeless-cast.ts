import { throwError } from "metano-runtime";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

export class Typeless {
  ofDefault(first: number | null): [bigint, bigint | null] {
    return [BigInt(first ?? throwError(new InvalidOperationException("Nullable object must have a value."))), null] as [bigint, bigint | null];
  }

  ofNull(first: number | null, second: number): [bigint, [bigint, string | null]] {
    return [BigInt(first ?? throwError(new InvalidOperationException("Nullable object must have a value."))), [BigInt(second), null] as [bigint, string | null]] as [bigint, [bigint, string | null]];
  }
}
