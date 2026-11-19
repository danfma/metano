import { Dictionary, HashSet, List } from "metano-runtime/system/collections";
import type { Animal } from "./animal";
import type { Cat } from "./cat";
import { Color } from "./color";
import type { Dog } from "./dog";

export class Shelf {
  private readonly _primes: HashSet<number> = HashSet.of<number>([2, 3, 5]);
  private readonly _colors: HashSet<Color> = HashSet.of<Color>([Color.Red]);
  private readonly _defaults: Map<string, number> = new Map<string, number>([["a", 1] as [string, number], ["b", 2] as [string, number]]);
  private readonly _cache: Map<string, number> = new Map<string, number>();
  private readonly _none: HashSet<string> = new HashSet<string>();
  private _calls: number = 0;

  constructor() { }

  private next(): number {
    return this._calls = (this._calls + 1) | 0;
  }

  union(xs: number[], ys: List<number>): HashSet<number> {
    return HashSet.of<number>([...xs, ...ys]);
  }

  mixed(xs: number[]): HashSet<number> {
    return HashSet.of<number>([this.next(), ...xs, this.next()]);
  }

  sorted(source: HashSet<number>): HashSet<number> {
    return new HashSet<number>([...source, 0]);
  }

  names(names: Iterable<string>): HashSet<string> {
    return new HashSet<string>(["root", ...names]);
  }

  count(set: HashSet<string>): number {
    return set.count;
  }

  pass(extra: string[]): number {
    return this.count(HashSet.of<string>(["a", ...extra]));
  }

  palette(extra: HashSet<Color>): HashSet<Color> {
    const palette = HashSet.of<Color>([Color.Green, ...extra, ...this._colors]);

    return palette;
  }

  optional(): HashSet<number | null> {
    const values = HashSet.of<number | null>([1]);
    values.add(null);

    return values;
  }

  pets(dog: Dog, cat: Cat): HashSet<Animal> {
    const pets = HashSet.of<Animal>([dog]);
    pets.add(cat);

    return pets;
  }

  filled(): HashSet<number> {
    const values = new HashSet<number>();
    values.add(1);

    return values;
  }

  merge(left: Dictionary<string, number>, right: Iterable<readonly [string, number]>): Map<string, number> {
    return new Map<string, number>([...left, ["k", 0] as [string, number], ...right, ...this._defaults]);
  }

  byIds(pairs: List<readonly [bigint, string]>): Map<bigint, string> {
    return new Map<bigint, string>([...pairs]);
  }
}
