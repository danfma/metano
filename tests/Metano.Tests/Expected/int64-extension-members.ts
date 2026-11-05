import { checkedInt64 } from "metano-runtime";
import type { Holder } from "./holder";
import { bonus$get, bonus$set, shared$get, shared$set } from "./holder-extensions";

export class Uses {
  constructor() { }

  bump(h: Holder): void {
    bonus$set(h, BigInt.asIntN(64, bonus$get(h) + 1n));
    bonus$set(h, BigInt.asIntN(64, bonus$get(h) + 5n));
    bonus$set(h, BigInt.asIntN(64, bonus$get(h) << 3n));
    bonus$set(h, bonus$get(h) / 2n);
    bonus$set(h, BigInt.asIntN(64, BigInt.asUintN(64, bonus$get(h)) >> 6n));
    shared$set(BigInt.asIntN(64, shared$get() - 1n));
    shared$set(BigInt.asIntN(64, shared$get() * 3n));
    shared$set(shared$get() % 7n);

    {
      bonus$set(h, checkedInt64(bonus$get(h) * 2n));
    }
  }
}
