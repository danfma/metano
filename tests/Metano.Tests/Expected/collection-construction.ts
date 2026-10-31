import { HashSet } from "metano-runtime";
import type { Tag } from "./tag";

export class Shelf {
  private readonly _items: number[] = [] as Array<number>;

  private readonly _tags: HashSet<Tag> = new HashSet<Tag>();

  private readonly _byName: Map<string, Tag | null> = new Map<string, Tag | null>();

  constructor() { }

  size(source: number[], capacity: number): number {
    const empty = [] as Array<string | null>;
    const sized = [] as Array<number>;
    const copy = [...source];
    const map = new Map<string, number>();
    const mapCopy = new Map<string, number>(map);
    const set = new HashSet<Tag>(this._tags);
    const pair = ["k", 1] as [string, number];
    const fromPairs = new Map<string, number>([] as Array<[string, number]>);
    const tuple = [1, "s"] as [number, string];

    return empty.length + sized.length + copy.length + map.size + mapCopy.size + set.size + pair[1] + pair[0].length + fromPairs.size + tuple[0] + tuple[1].length + this._items.length + this._byName.size;
  }
}
