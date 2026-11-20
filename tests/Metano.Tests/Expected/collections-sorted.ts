import { SortedDictionary, SortedList, SortedSet, type IComparer, type IDictionary } from "metano-runtime/system/collections";

export class Ordered {
  constructor() { }

  static set(): SortedSet<number> {
    return SortedSet.of<number>([3, 1, 2]);
  }

  static setWithComparer(comparer: IComparer<string>): SortedSet<string> {
    return SortedSet.of<string>(["b", "a"], comparer);
  }

  static setCopy(items: Iterable<number>): SortedSet<number> {
    return SortedSet.from(items);
  }

  static setExpression(more: number[]): SortedSet<number> {
    return SortedSet.of<number>([5, ...more]);
  }

  static setReads(set: SortedSet<number>): string {
    set.add(4);
    set.remove(1);
    const view = set.getViewBetween(2, 4);

    return `${set.minOr(0)}:${set.maxOr(0)}:${set.count}:${view.count}:${[...set.reverse()].join(",")}:${set.removeWhere((x: number) => x > 10)}`;
  }

  static smallest(set: SortedSet<string>): string | null {
    return set.minOr(null);
  }

  static dictionary(): SortedDictionary<string, number> {
    return SortedDictionary.assign<string, number>([
      ["b", 2],
      ["a", 1],
    ]);
  }

  static dictionaryAdded(): SortedDictionary<string, number> {
    return SortedDictionary.of<string, number>([
      ["b", 2],
      ["a", 1],
    ]);
  }

  static dictionaryCopy(source: IDictionary<string, number>, comparer: IComparer<string>): SortedDictionary<string, number> {
    return SortedDictionary.from(source, comparer);
  }

  static dictionaryReads(map: SortedDictionary<string, number>): string {
    map.set("c", 3);
    map.set("a", (map.get("a") + 10) | 0);
    const [$found0, b] = map.tryGetValue("b", 0);
    const found = $found0 ? b : -1;

    return `${map.get("a")}:${map.count}:${[...map.keys].join(",")}:${map.values.count}:${map.getValueOrDefault("z", 0)}:${found}`;
  }

  static list(capacity: number): SortedList<string, number> {
    let $t;

    return ((receiver$temp$0) => {
      (receiver$temp$0.set("x", $t = 1), $t);

      return receiver$temp$0;
    })((SortedList.withCapacity<string, number>(capacity)));
  }

  static listExpression(): SortedList<string, number> {
    return SortedList.assign<string, number>([
      ["y", 2],
    ]);
  }

  static listReads(list: SortedList<string, number>): string {
    return `${list.keys.get(0)}:${list.values.get(0)}:${list.getKeyAtIndex(0)}:${list.indexOfKey("x")}:${list.indexOfValue(1)}`;
  }
}
