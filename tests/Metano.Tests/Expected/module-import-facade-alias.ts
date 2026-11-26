import { fooS, fooY$, otherS, otherX$ } from "./res.js";
import { fooY, other as otherX, otherS as thing } from "ext-lib";

export class Use {
  go(): number {
    return (otherX(1) + thing("t") + fooY(2) + otherX$(1) + otherS("a") + fooY$(3) + fooS("b")) | 0;
  }
}
