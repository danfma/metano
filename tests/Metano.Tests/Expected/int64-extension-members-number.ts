import { intDiv } from "metano-runtime";
import type { Holder } from "./holder";
import { bonus$get, bonus$set, nBonus$get, nBonus$set, nShared$get, nShared$set, shared$get, shared$set } from "./holder-extensions";

export class Uses {
  constructor() { }

  bump(h: Holder): void {
    bonus$set(h, bonus$get(h) + 1);
    bonus$set(h, bonus$get(h) + 5);
    bonus$set(h, bonus$get(h) << 3);
    bonus$set(h, intDiv(bonus$get(h), 2));
    bonus$set(h, bonus$get(h) >>> 70);
    nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! + 1);
    nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! + 2);
    nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! - 1);
    nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! * 2);
    nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! << 3);
    nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! % 4);
    shared$set(shared$get() - 1);
    shared$set(shared$get() * 3);
    shared$set(shared$get() % 7);
    nShared$set(nShared$get() == null ? null : nShared$get()! + 1);
    nShared$set(nShared$get() == null ? null : nShared$get()! - 4);

    {
      bonus$set(h, bonus$get(h) * 2);
      nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! + 5);
      nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! + 1);
    }
  }

  bumpMaybe(h: Holder | null): void {
    h != null && nBonus$set(h, nBonus$get(h) == null ? null : nBonus$get(h)! + 2);
  }
}
