import { HashSet } from "metano-runtime";
import { Color } from "./color";

export class Shelf {
  private readonly _primes: HashSet<number> = new HashSet([2, 3, 5]);

  private readonly _colors: HashSet<Color> = new HashSet([Color.Red]);

  private readonly _defaults: Map<string, number> = new Map([["a", 1] as [string, number], ["b", 2] as [string, number]]);

  private readonly _cache: Map<string, number> = new Map();

  private readonly _none: HashSet<string> = new HashSet();

  private _calls: number = 0;

  constructor() { }

  private next(): number {
    return ++this._calls;
  }

  union(xs: number[], ys: number[]): HashSet<number> {
    return new HashSet([...xs, ...ys]);
  }

  mixed(xs: number[]): HashSet<number> {
    return new HashSet([this.next(), ...xs, this.next()]);
  }

  sorted(source: HashSet<number>): HashSet<number> {
    return new HashSet([...source, 0]);
  }

  names(names: Iterable<string>): HashSet<string> {
    return new HashSet(["root", ...names]);
  }

  count(set: HashSet<string>): number {
    return set.size;
  }

  pass(extra: string[]): number {
    return this.count(new HashSet(["a", ...extra]));
  }

  palette(extra: HashSet<Color>): HashSet<Color> {
    const palette = new HashSet([Color.Green, ...extra, ...this._colors]);

    return palette;
  }

  merge(left: Map<string, number>, right: Iterable<[string, number]>): Map<string, number> {
    return new Map([...left, ["k", 0] as [string, number], ...right, ...this._defaults]);
  }

  byIds(pairs: [number, string][]): Map<number, string> {
    return new Map([...pairs]);
  }
}
