import { checkedInt64 } from "metano-runtime";
import type { Holder } from "./holder";
import { bonus$get, bonus$set, nBonus$get, nBonus$set, nShared$get, nShared$set, shared$get, shared$set } from "./holder-extensions";

export class Uses {
  constructor() { }

  bump(h: Holder): void {
    bonus$set(h, BigInt.asIntN(64, bonus$get(h) + 1n));
    bonus$set(h, BigInt.asIntN(64, bonus$get(h) + 5n));
    bonus$set(h, BigInt.asIntN(64, bonus$get(h) << 3n));
    bonus$set(h, bonus$get(h) / 2n);
    bonus$set(h, BigInt.asIntN(64, BigInt.asUintN(64, bonus$get(h)) >> 6n));
    nBonus$set(h, nBonus$get(h) == null ? null : BigInt.asIntN(64, nBonus$get(h)! + 1n));
    nBonus$set(h, nBonus$get(h) == null ? null : BigInt.asIntN(64, nBonus$get(h)! + 2n));
    nBonus$set(h, nBonus$get(h) == null ? null : BigInt.asIntN(64, nBonus$get(h)! - 1n));
    nBonus$set(h, nBonus$get(h) == null ? null : BigInt.asIntN(64, nBonus$get(h)! * 2n));
    nBonus$set(h, nBonus$get(h) == null ? null : BigInt.asIntN(64, nBonus$get(h)! << 3n));
    nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! % 4n);
    shared$set(BigInt.asIntN(64, shared$get() - 1n));
    shared$set(BigInt.asIntN(64, shared$get() * 3n));
    shared$set(shared$get() % 7n);
    nShared$set(nShared$get() == null ? null : BigInt.asIntN(64, nShared$get()! + 1n));
    nShared$set(nShared$get() == null ? null : BigInt.asIntN(64, nShared$get()! - 4n));

    {
      bonus$set(h, checkedInt64(bonus$get(h) * 2n));
      nBonus$set(h, nBonus$get(h) == null ? null : checkedInt64(nBonus$get(h)! + 5n));
      nBonus$set(h, nBonus$get(h) == null ? null : checkedInt64(nBonus$get(h)! + 1n));
    }
  }

  bumpMaybe(h: Holder | null): void {
    h != null && nBonus$set(h, nBonus$get(h) == null ? null : BigInt.asIntN(64, nBonus$get(h)! + 2n));
  }
}
