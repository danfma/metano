import { fooS, fooY$, otherS, otherX$ } from "./res";
import { fooY, other as otherX, otherS as thing } from "ext-lib";

export class Use {
  constructor() { }

  go(): number {
    return otherX(1) + thing("t") + fooY(2) + otherX$(1) + otherS("a") + fooY$(3) + fooS("b");
  }
}
