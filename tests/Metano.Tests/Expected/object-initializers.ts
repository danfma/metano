import { Dictionary, HashSet, List } from "metano-runtime/system/collections";
import { Bag } from "./bag.js";
import { Point } from "./point.js";
import { Tag } from "./tag.js";

export class Factory {
  private readonly _defaults: List<number> = List.of<number>([7, 8]);
  seed: number = 0;

  constructor() { }

  build(capacity: number): number {
    const point = ((receiver$temp$0) => {
      receiver$temp$0.x = (this.seed + 1) | 0;
      Object.assign(receiver$temp$0, { y: 2 });
      receiver$temp$0.inner.z = 3;
      receiver$temp$0.inner.items.add(4);
      receiver$temp$0.inner.items.add(5);

      return receiver$temp$0;
    })(Point.fromSeed(this.seed));
    const tag = ((receiver$temp$1) => {
      Object.assign(receiver$temp$1, { name: "u" });
      Object.assign(receiver$temp$1, { weight: 1 });

      return receiver$temp$1;
    })(new Tag("t"));
    const list = List.of<number>([1, 2]);
    const sized = (List.withCapacity<number>(capacity)).initAdd([3]);
    const typed = List.of<string>(["a"]);
    const byIndex = Dictionary.assign<string, number>([
      ["a", 1],
      ["b", 2],
    ]);
    const byAdd = Dictionary.of<string, number>([
      ["c", 3],
    ]);
    const set = HashSet.of<string>(["x", "y"]);
    const nested = List.of<List<number>>([List.of<number>([1]), List.of<number>([2])]);
    const bag = ((receiver$temp$2) => {
      receiver$temp$2.add("a");
      receiver$temp$2.add("b", 2);

      return receiver$temp$2;
    })(new Bag());

    return (point.x + tag.weight + list.count + sized.count + typed.count + byIndex.count + byAdd.count + set.count + nested.count + this._defaults.count) | 0;
  }
}
