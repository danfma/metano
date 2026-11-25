import { Dictionary, HashSet, List } from "metano-runtime/system/collections";
import type { Tag } from "./tag.js";

export class Shelf {
  declare private readonly _items: List<number>;
  declare private readonly _tags: HashSet<Tag>;
  declare private readonly _byName: Dictionary<string, Tag | null>;

  constructor() {
    this._items = new List<number>();
    this._tags = HashSet.withCapacity<Tag>(4);
    this._byName = (new Dictionary<string, Tag | null>());
  }

  size(source: List<number>, capacity: number): number {
    const empty = new List<string | null>();
    const sized = List.withCapacity<number>(capacity);
    const copy = List.from(source);
    const map = Dictionary.withCapacity<string, number>(capacity);
    const mapCopy = Dictionary.from(map);
    const set = HashSet.from(this._tags);
    const pair = ["k", 1] as [string, number];
    const fromPairs = Dictionary.from(new List<readonly [string, number]>());
    const tuple = [1, "s"] as [number, string];

    return (empty.count + sized.count + copy.count + map.count + mapCopy.count + set.count + pair[1] + pair[0].length + fromPairs.count + tuple[0] + tuple[1].length + this._items.count + this._byName.count) | 0;
  }
}
