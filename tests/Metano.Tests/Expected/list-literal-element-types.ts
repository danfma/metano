import type { Animal } from "./animal";
import type { Cat } from "./cat";
import type { Dog } from "./dog";
import { Holder } from "./holder";

export class Lists {
  private readonly _field: (number | null)[] = [1];

  private _pets: Animal[] = [];

  constructor() { }

  nullableLocal(): number {
    const l: (number | null)[] = [1];
    l.push(null);

    return l.length;
  }

  polymorphicLocal(dog: Dog, cat: Cat): number {
    const l: Animal[] = [dog];
    l.push(cat);

    return l.length;
  }

  emptyLocal(): number {
    const l: number[] = [];
    l.push(1);

    return l.length;
  }

  nullableReference(maybe: string | null): number {
    const named: (string | null)[] = ["a"];
    const same = [maybe];
    named.push(null);

    return named.length + same.length;
  }

  spreadLocal(xs: number[]): number {
    const l: (number | null)[] = [...xs];
    l.push(null);

    return l.length;
  }

  reassigned(): number {
    let l = [1] as (number | null)[];
    l = [null];

    return [...l].length;
  }

  nullableArray(): number {
    const a: (number | null)[] = [1];
    a[0] = null;

    return a.length;
  }

  arrayCreation(dog: Dog, cat: Cat): number {
    const a = [dog] as Animal[];
    const same = [dog];
    const empty = [] as number[];
    a[0] = cat;

    return a.length + same.length + empty.length;
  }

  nested(): number {
    const l = [[1] as (number | null)[]];
    l[0].push(null);

    return l[0].length;
  }

  cast(): number {
    const l = [1] as (number | null)[];
    l.push(null);

    return l.length;
  }

  conditional(flag: boolean): number {
    const l = flag ? [1] as (number | null)[] : [2, 3] as (number | null)[];
    l.push(null);

    return l.length;
  }

  lambda(): number {
    const make = () => [1] as (number | null)[];
    const l = make();
    l.push(null);

    return l.length;
  }

  exact(): number {
    const ints = [1, 2];
    const doubles = [1, 2.5];
    const names = ["a"];

    return ints.length + doubles.length + names.length;
  }

  readOnlyViews(dog: Dog): number {
    const counted = [] as number[];
    const sequence = [dog] as Animal[];
    const collection: (number | null)[] = [1];
    const read: Animal[] = [dog];
    const list: (number | null)[] = [1];
    collection.push(null);
    list[0] = null;
    const first = read[0].name.length;

    return counted.length + collection.length + read.length + list.length + first;
  }

  erasedCasts(): number {
    const cast: (number | null)[] = [1];
    const defaulted: (number | null)[] = [null];
    cast.push(null);
    defaulted.push(1);

    return cast.length + defaulted.length;
  }

  nestedNullability(): number {
    const pairs: [number, string | null][] = [[1, "a"] as [number, string]];
    const exact = [[1, "a"] as [number, string]];
    pairs.push([2, null] as [number, string | null]);

    return pairs.length + exact.length;
  }

  forHeader(): number {
    let total = 0;
    for (let l: (number | null)[] = [1]; l.length < 3; l.push(null)) {
      total += l.length;
    }

    return total;
  }

  numeric(ints: number[]): number {
    const bytes = [1, 2];
    const longs = [1n, 2n];
    const widened = [...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))];

    return bytes.length + longs.length + widened.length;
  }

  walk(given: (number | null)[] | null, kind: number): number {
    const parenthesized: (number | null)[] = [1];
    const coalesced = given ?? [1] as (number | null)[];
    const switched = kind === 0 ? [1] as (number | null)[] : [2, 3] as (number | null)[];
    const block = () => [1] as (number | null)[];
    const holder = ((receiver$temp$0) => {
      receiver$temp$0.items = [1];

      return receiver$temp$0;
    })(new Holder());
    const lists = ((receiver$temp$1) => {
      receiver$temp$1.push([1]);

      return receiver$temp$1;
    })([] as Array<(number | null)[]>);
    parenthesized.push(null);
    coalesced.push(null);
    switched.push(null);
    block().push(null);
    holder.items.push(null);
    holder.initial.push(null);
    lists[0].push(null);

    return parenthesized.length + coalesced.length + switched.length + lists.length;
  }

  returned(): (number | null)[] {
    return [1];
  }

  returnedFromABlock(): (number | null)[] {
    return [];
  }

  take(l: (number | null)[]): number {
    return l.length;
  }

  argument(): number {
    return this.take([1]);
  }

  assigned(dog: Dog, cat: Cat): number {
    this._pets = [dog];
    this._pets.push(cat);
    this._field.push(null);

    return this._pets.length + this._field.length;
  }
}
