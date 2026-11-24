import { List } from "metano-runtime/system/collections";
import { firstOrDefault, lastOrDefault, linq, singleOrDefault } from "metano-runtime/system/linq";
import type { Point } from "./point.js";
import { Tone } from "./tone.js";

export class Zeroed {
  constructor() { }

  at(xs: List<Point>): Point | null {
    return linq(xs, firstOrDefault());
  }

  atDefault(xs: List<Point>): Point | null {
    return linq(xs, firstOrDefault((p: Point) => p.x > 1));
  }

  atParenthesized(xs: List<Point>): Point | null {
    return linq(xs, firstOrDefault());
  }

  atForgiven(xs: List<Point>): Point | null {
    return linq(xs, lastOrDefault((p: Point) => p.x > 1));
  }

  pair(xs: List<[number, number]>): [number, number] | null {
    return linq(xs, lastOrDefault((x: [number, number]) => x[0] > 1, [0, 0] as [number, number]));
  }

  slot<T>(xs: List<T>): T | null {
    return linq(xs, singleOrDefault());
  }

  voice(xs: List<Tone>): Tone | null {
    return linq(xs, firstOrDefault());
  }

  found(xs: List<Point>): Point | null {
    return xs.find((p: Point) => p.x > 1, null);
  }

  foundVoice(xs: List<Tone>): Tone | null {
    return xs.find((t: Tone) => t !== Tone.Loud, null);
  }
}
