import { ConcurrentDictionary, Dictionary, HashSet, ImmutableDictionary, ImmutableHashSet, List, SortedSet } from "metano-runtime/system/collections";
import type { Animal } from "./animal.js";
import type { Cat } from "./cat.js";
import { Color } from "./color.js";
import type { Dog } from "./dog.js";

export class Shelf {
  private readonly _primes: HashSet<number> = HashSet.of<number>([2, 3, 5]);
  private readonly _colors: HashSet<Color> = HashSet.of<Color>([Color.Red]);
  private readonly _defaults: ImmutableDictionary<string, number> = ImmutableDictionary.empty<string, number>().setItems([["a", 1] as [string, number], ["b", 2] as [string, number]]);
  private readonly _cache: ConcurrentDictionary<string, number> = new ConcurrentDictionary<string, number>();
  private readonly _none: ImmutableHashSet<string> = ImmutableHashSet.empty<string>();
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

  sorted(source: HashSet<number>): SortedSet<number> {
    return SortedSet.of<number>([...source, 0]);
  }

  names(names: Iterable<string>): ImmutableHashSet<string> {
    return ImmutableHashSet.of<string>(["root", ...names]);
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

  merge(left: Dictionary<string, number>, right: Iterable<readonly [string, number]>): ImmutableDictionary<string, number> {
    return ImmutableDictionary.empty<string, number>().setItems([...left, ["k", 0] as [string, number], ...right, ...this._defaults]);
  }

  byIds(pairs: List<readonly [bigint, string]>): ImmutableDictionary<bigint, string> {
    return ImmutableDictionary.empty<bigint, string>().setItems([...pairs]);
  }
}
