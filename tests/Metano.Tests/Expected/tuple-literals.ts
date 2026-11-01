import type { Point } from "./point";

export class Pairs {
  constructor() { }

  positional(a: number): [number, string] {
    return [a, "x"] as [number, string];
  }

  named(): [number, number] {
    return [1, 2] as [number, number];
  }

  converted(p: Point): [string | null, Point] {
    return [null, p] as [string | null, Point];
  }

  reads(): number {
    const named = this.named();
    const positional = this.positional(3);
    const local = [4, "s"] as [number, string];

    return named[0] + named[1] + positional[0] + positional[1].length + local[0] + local[1].length;
  }
}
