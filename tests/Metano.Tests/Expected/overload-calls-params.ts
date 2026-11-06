import { Extra } from "./extra";

export class Use {
  constructor() { }

  expanded(): number {
    return Extra.p(1, 2, 3);
  }

  pair(): number {
    return Extra.p(5, 6);
  }

  alone(): number {
    return Extra.p(5);
  }

  spread(items: number[]): number {
    return Extra.p(1, ...items);
  }

  empty(): number {
    return Extra.parts();
  }

  several(): number {
    return Extra.parts(1, 2, 3);
  }

  arr(xs: number[]): number {
    return Extra.parts(...xs);
  }
}
