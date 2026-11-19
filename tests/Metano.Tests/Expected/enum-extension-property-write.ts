import { shade$get, shade$set } from "./cell-extensions";
import type { Shade } from "./shade";

export class Caller {
  constructor() { }

  compound(c: Cell): void {
    shade$set(c, ((shade$get(c) + 1) | 0) as Shade);
  }

  increment(c: Cell): void {
    shade$set(c, ((shade$get(c) + 1) | 0) as Shade);
  }
}
