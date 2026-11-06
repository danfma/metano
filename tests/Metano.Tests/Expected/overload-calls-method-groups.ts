import type { Base } from "./base";

export class Use {
  constructor() { }

  of(b: Base): (arg: number) => number {
    return b.addX.bind(b);
  }

  ofString(b: Base): (arg: string) => number {
    return b.addS.bind(b);
  }

  static staticBound(b: Base): (arg: number) => number {
    return b.addX.bind(b);
  }
}
