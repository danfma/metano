import { List } from "metano-runtime/system/collections";
import { Color } from "./color";
import { Level } from "./level";
import type { Person } from "./person";

export class Finders {
  constructor() { }

  found(xs: List<number>): number {
    return xs.find((x: number) => x > 100, 0);
  }

  foundWide(xs: List<bigint>): bigint {
    return BigInt.asIntN(64, xs.find((x: bigint) => x > 100n, 0n) + 3n);
  }

  foundZero(xs: List<Color>): Color {
    return xs.find((c: Color) => c !== Color.Red, Color.None);
  }

  foundNoZero(xs: List<Level>): Level {
    return xs.find((l: Level) => l !== Level.Low, 0 as Level);
  }

  foundWho(xs: List<Person>): Person | null {
    return xs.find((p: Person) => p.name === "ana", null);
  }

  foundIn(xs: List<number> | null): number | null {
    return xs?.find((x: number) => x > 100, 0) ?? null;
  }
}
