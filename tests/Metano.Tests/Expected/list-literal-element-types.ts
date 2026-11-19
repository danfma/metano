import { List } from "metano-runtime/system/collections";
import type { Animal } from "./animal";
import type { Cat } from "./cat";
import type { Dog } from "./dog";
import { Holder } from "./holder";

export class Lists {
  private readonly _field: List<number | null> = List.of([1]);
  private _pets: List<Animal> = List.of([]);

  constructor() { }

  nullableLocal(): number {
    const l = List.of([1] as (number | null)[]);
    l.add(null);

    return l.count;
  }

  polymorphicLocal(dog: Dog, cat: Cat): number {
    const l = List.of([dog] as Animal[]);
    l.add(cat);

    return l.count;
  }

  emptyLocal(): number {
    const l = List.of([] as number[]);
    l.add(1);

    return l.count;
  }

  nullableReference(maybe: string | null): number {
    const named = List.of(["a"] as (string | null)[]);
    const same = List.of([maybe]);
    named.add(null);

    return (named.count + same.count) | 0;
  }

  spreadLocal(xs: number[]): number {
    const l = List.of([...xs] as (number | null)[]);
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
    const l = List.of([List.of([1] as (number | null)[])]);
    l.get(0).add(null);

    return l.get(0).count;
  }

  cast(): number {
    const l = List.of([1] as (number | null)[]);
    l.add(null);

    return l.count;
  }

  conditional(flag: boolean): number {
    const l = flag ? List.of([1] as (number | null)[]) : List.of([2, 3] as (number | null)[]);
    l.add(null);

    return l.count;
  }

  lambda(): number {
    const make = () => List.of([1] as (number | null)[]);
    const l = make();
    l.add(null);

    return l.count;
  }

  exact(): number {
    const ints = List.of([1, 2]);
    const doubles = List.of([1, 2.5]);
    const names = List.of(["a"]);

    return (ints.count + doubles.count + names.count) | 0;
  }

  readOnlyViews(dog: Dog): number {
    const counted = List.adopt([] as number[], { fixedSize: true });
    const sequence = [dog] as Animal[];
    const collection = List.of([1] as (number | null)[]);
    const read = List.adopt([dog] as Animal[], { fixedSize: true });
    const list = List.of([1] as (number | null)[]);
    collection.add(null);
    list.set(0, null);
    const first = read.get(0).name.length;

    return (counted.count + collection.count + read.count + list.count + first) | 0;
  }

  erasedCasts(): number {
    const cast = List.of([1] as (number | null)[]);
    const defaulted = List.of([null] as (number | null)[]);
    cast.add(null);
    defaulted.add(1);

    return (cast.count + defaulted.count) | 0;
  }

  nestedNullability(): number {
    const pairs = List.of([[1, "a"] as [number, string]] as [number, string | null][]);
    const exact = List.of([[1, "a"] as [number, string]]);
    pairs.add([2, null] as [number, string | null]);

    return (pairs.count + exact.count) | 0;
  }

  forHeader(): number {
    let total = 0;
    for (let l = List.of([1] as (number | null)[]); l.count < 3; l.add(null)) {
      total = (total + l.count) | 0;
    }

    return total;
  }

  numeric(ints: number[]): number {
    const bytes = [1, 2];
    const longs = List.of([1n, 2n]);
    const widened = List.of([...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))]);

    return (bytes.length + longs.count + widened.count) | 0;
  }

  walk(given: List<number | null> | null, kind: number): number {
    const parenthesized = List.of([1] as (number | null)[]);
    const coalesced = given ?? List.of([1] as (number | null)[]);
    const switched = kind === 0 ? List.of([1] as (number | null)[]) : List.of([2, 3] as (number | null)[]);
    const block = () => List.of([1] as (number | null)[]);
    const holder = ((receiver$temp$0) => {
      receiver$temp$0.items = List.of([1]);

      return receiver$temp$0;
    })(new Holder());
    const lists = List.of([List.of([1])]);
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
    return List.of([1]);
  }

  returnedFromABlock(): List<number | null> {
    return List.of([]);
  }

  take(l: List<number | null>): number {
    return l.count;
  }

  argument(): number {
    return this.take(List.of([1]));
  }

  assigned(dog: Dog, cat: Cat): number {
    this._pets = List.of([dog]);
    this._pets.add(cat);
    this._field.add(null);

    return (this._pets.count + this._field.count) | 0;
  }
}
