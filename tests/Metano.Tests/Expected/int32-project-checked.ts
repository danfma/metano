import { checkedInt32, checkedNarrowInt32 } from "metano-runtime";

export class Project {
  constructor() { }

  add(a: number, b: number): number {
    return checkedInt32(a + b);
  }

  narrow(x: number): number {
    return checkedNarrowInt32(x, 8, false);
  }

  step(b: number): number {
    b = checkedNarrowInt32(b + 1, 8, false);

    return b;
  }

  wrapped(a: number, b: number): number {
    return a + b;
  }

  kept(x: number): number {
    return x & 0xFF;
  }
}
