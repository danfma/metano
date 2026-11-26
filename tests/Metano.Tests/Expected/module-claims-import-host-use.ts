import { fooS, fooX$ } from "./res.js";
import { fooX } from "ext-lib";

export class Use {
  go(): number {
    return (fooX(1) + fooX$(1) + fooS("a")) | 0;
  }
}
