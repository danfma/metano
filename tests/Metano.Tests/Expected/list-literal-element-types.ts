import { List } from "metano-runtime/system/collections";
import type { Animal } from "./animal.js";
import type { Cat } from "./cat.js";
import type { Dog } from "./dog.js";
import { Holder } from "./holder.js";

export class Lists {
  declare private readonly _field: List<number | null>;
  declare private _pets: List<Animal>;

  constructor() {
    this._field = List.of<number | null>([1]);
    this._pets = List.of<Animal>([]);
  }

  nullableLocal(): number {
    const l = List.of<number | null>([1]);
    l.add(null);

    return l.count;
  }

  polymorphicLocal(dog: Dog, cat: Cat): number {
    const l = List.of<Animal>([dog]);
    l.add(cat);

    return l.count;
  }

  emptyLocal(): number {
    const l = List.of<number>([]);
    l.add(1);

    return l.count;
  }

  nullableReference(maybe: string | null): number {
    const named = List.of<string | null>(["a"]);
    const same = List.of<string | null>([maybe]);
    named.add(null);

    return (named.count + same.count) | 0;
  }

  spreadLocal(xs: number[]): number {
    const l = List.of<number | null>([...xs]);
    l.add(null);

    return l.count;
  }

  reassigned(): number {
    let l = [1] as (number | null)[];
    l = [null];

    return List.from(l).count;
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

    return (a.length + same.length + empty.length) | 0;
  }

  nested(): number {
    const l = List.of<List<number | null>>([List.of<number | null>([1])]);
    l.get(0).add(null);

    return l.get(0).count;
  }

  cast(): number {
    const l = List.of<number | null>([1]);
    l.add(null);

    return l.count;
  }

  conditional(flag: boolean): number {
    const l = flag ? List.of<number | null>([1]) : List.of<number | null>([2, 3]);
    l.add(null);

    return l.count;
  }

  lambda(): number {
    const make = () => List.of<number | null>([1]);
    const l = make();
    l.add(null);

    return l.count;
  }

  exact(): number {
    const ints = List.of<number>([1, 2]);
    const doubles = List.of<number>([1, 2.5]);
    const names = List.of<string>(["a"]);

    return (ints.count + doubles.count + names.count) | 0;
  }

  readOnlyViews(dog: Dog): number {
    const counted = List.adopt<number>([], { fixedSize: true });
    const sequence = [dog] as Animal[];
    const collection = List.of<number | null>([1]);
    const read = List.adopt<Animal>([dog], { fixedSize: true });
    const list = List.of<number | null>([1]);
    collection.add(null);
    list.set(0, null);
    const first = read.get(0).name.length;

    return (counted.count + collection.count + read.count + list.count + first) | 0;
  }

  erasedCasts(): number {
    const cast = List.of<number | null>([1]);
    const defaulted = List.of<number | null>([null]);
    cast.add(null);
    defaulted.add(1);

    return (cast.count + defaulted.count) | 0;
  }

  nestedNullability(): number {
    const pairs = List.of<[number, string | null]>([[1, "a"] as [number, string]]);
    const exact = List.of<[number, string]>([[1, "a"] as [number, string]]);
    pairs.add([2, null] as [number, string | null]);

    return (pairs.count + exact.count) | 0;
  }

  forHeader(): number {
    let total = 0;
    for (let l = List.of<number | null>([1]); l.count < 3; l.add(null)) {
      total = (total + l.count) | 0;
    }

    return total;
  }

  numeric(ints: number[]): number {
    const bytes = [1, 2];
    const longs = List.of<bigint>([1n, 2n]);
    const widened = List.of<bigint>([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);

    return (bytes.length + longs.count + widened.count) | 0;
  }

  walk(given: List<number | null> | null, kind: number): number {
    const parenthesized = List.of<number | null>([1]);
    const coalesced = given ?? List.of<number | null>([1]);
    const switched = kind === 0 ? List.of<number | null>([1]) : List.of<number | null>([2, 3]);
    const block = () => List.of<number | null>([1]);
    const receiver$temp$0 = new Holder();
    receiver$temp$0.items = List.of<number | null>([1]);
    const holder = receiver$temp$0;
    const lists = List.of<List<number | null>>([List.of<number | null>([1])]);
    parenthesized.add(null);
    coalesced.add(null);
    switched.add(null);
    block().add(null);
    holder.items.add(null);
    holder.initial.add(null);
    lists.get(0).add(null);

    return (parenthesized.count + coalesced.count + switched.count + lists.count) | 0;
  }

  returned(): List<number | null> {
    return List.of<number | null>([1]);
  }

  returnedFromABlock(): List<number | null> {
    return List.of<number | null>([]);
  }

  take(l: List<number | null>): number {
    return l.count;
  }

  argument(): number {
    return this.take(List.of<number | null>([1]));
  }

  assigned(dog: Dog, cat: Cat): number {
    this._pets = List.of<Animal>([dog]);
    this._pets.add(cat);
    this._field.add(null);

    return (this._pets.count + this._field.count) | 0;
  }
}
