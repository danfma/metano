import { fooS, fooX$ } from "./res";
import { fooX } from "ext-lib";

export class Use {
  constructor() { }

  go(): number {
    return fooX(1) + fooX$(1) + fooS("a");
  }
}
