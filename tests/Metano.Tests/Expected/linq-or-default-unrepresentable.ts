import { firstOrDefault, lastOrDefault, linq, singleOrDefault } from "metano-runtime/system/linq";
import type { Point } from "./point";
import { Tone } from "./tone";

export class Zeroed {
  constructor() { }

  at(xs: Point[]): Point | null {
    return linq(xs, firstOrDefault());
  }

  atDefault(xs: Point[]): Point | null {
    return linq(xs, firstOrDefault((p: Point) => p.x > 1));
  }

  pair(xs: [number, number][]): [number, number] | null {
    return linq(xs, lastOrDefault((x: [number, number]) => x[0] > 1));
  }

  slot<T>(xs: T[]): T | null {
    return linq(xs, singleOrDefault());
  }

  voice(xs: Tone[]): Tone | null {
    return linq(xs, firstOrDefault());
  }

  found(xs: Point[]): Point | null {
    return (xs.find((p: Point) => p.x > 1) ?? null);
  }

  foundVoice(xs: Tone[]): Tone | null {
    return (xs.find((t: Tone) => t !== Tone.Loud) ?? null);
  }
}
