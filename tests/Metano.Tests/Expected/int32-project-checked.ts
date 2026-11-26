import { checkedInt32, checkedNarrowNumber } from "metano-runtime";

export class Project {
  add(a: number, b: number): number {
    return checkedInt32(a + b);
  }

  narrow(x: number): number {
    return checkedNarrowNumber(x, 8, false);
  }

  step(b: number): number {
    b = checkedNarrowNumber(b + 1, 8, false);

    return b;
  }

  wrapped(a: number, b: number): number {
    return (a + b) | 0;
  }

  kept(x: number): number {
    return x & 0xFF;
  }
}
