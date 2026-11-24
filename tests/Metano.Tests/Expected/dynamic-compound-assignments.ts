import type { Bag } from "./bag.js";

export class Stores {
  constructor() { }

  run(d: any, bag: Bag, i: number, c: string, s: number, u: number, x: number): void {
    twice$set(bag, (twice$get(bag) + d) | 0);
    i = (i + d) | 0;
    c = String.fromCharCode((c.charCodeAt(0) + d) & 0xFFFF);
    s = ((s + d) << 16) >> 16;
    u = (u - d) >>> 0;
    d += 1;
    const same = x === d;
    const less = x < d;
  }
}
