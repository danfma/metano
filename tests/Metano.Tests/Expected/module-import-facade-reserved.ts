import { quxS, quxX$ } from "./q.js";
import { barS, barX, bazS, bazX, fooS, fooX$ } from "./res.js";
import { fooX, quxX } from "ext-lib";

export class Use {
  go(): number {
    return (fooX(1) + (2 + 1) + fooX$(1) + fooS("a") + barX(1) + barS("b") + bazX(1) + bazS("c") + quxX(5) + quxX$(1) + quxS("z")) | 0;
  }
}
