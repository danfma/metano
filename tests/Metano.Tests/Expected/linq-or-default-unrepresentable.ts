import { firstOrDefault, lastOrDefault, linq, singleOrDefault } from "metano-runtime/system/linq";
import type { Point } from "./point";

export class Zeroed {
  constructor() { }

  at(xs: Point[]): Point | null {
    return linq(xs, firstOrDefault());
  }

  pair(xs: [number, number][]): [number, number] | null {
    return linq(xs, lastOrDefault((x: [number, number]) => x[0] > 1));
  }

  slot<T>(xs: T[]): T | null {
    return linq(xs, singleOrDefault());
  }

  found(xs: Point[]): Point | null {
    return (xs.find((p: Point) => p.x > 1) ?? null);
  }
}
