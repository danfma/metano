import { shade$get, shade$set } from "./cell-extensions.js";
import type { Shade } from "./shade.js";

export class Caller {
  constructor() { }

  compound(c: Cell): void {
    shade$set(c, ((shade$get(c) + 1) | 0) as Shade);
  }

  increment(c: Cell): void {
    shade$set(c, ((shade$get(c) + 1) | 0) as Shade);
  }
}
