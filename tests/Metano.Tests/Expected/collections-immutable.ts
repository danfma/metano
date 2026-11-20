import { Dictionary, ImmutableArray, ImmutableArrayBuilder, ImmutableDictionary, ImmutableDictionaryBuilder, ImmutableHashSet, ImmutableList, ImmutableListBuilder, List, toImmutableArray, toImmutableDictionary, toImmutableList, type IImmutableList } from "metano-runtime/system/collections";
import { formatBoolean } from "metano-runtime";

export class Snapshots {
  constructor() { }

  static listed(source: number[]): ImmutableList<number> {
    return ImmutableList.from<number>(source);
  }

  static fresh(): ImmutableList<number> {
    return ImmutableList.of<number>([1, 2, 3]);
  }

  static expression(): ImmutableList<string> {
    return ImmutableList.of<string>(["a", "b"]);
  }

  static none(): ImmutableList<number> {
    return ImmutableList.empty<number>();
  }

  static edited(list: ImmutableList<number>): ImmutableList<number> {
    return list.add(4).addRange([5, 6]).insert(0, 0).remove(2).removeAt(0).setItem(0, 9).removeItems([5]).removeRange(0, 1).sort().reverse();
  }

  static searches(list: ImmutableList<number>): number {
    return (list.indexOf(3) + list.find((x: number) => x > 1, 0) + list.findIndex((x: number) => x > 1) + list.binarySearch(3) + list.get(0) + list.fromEnd(1) + list.count) | 0;
  }

  static built(list: ImmutableList<number>): ImmutableList<number> {
    const builder = new ImmutableListBuilder(list);
    builder.add(7);
    builder.set(0, 1);

    return toImmutableList(builder);
  }

  static array(source: number[]): ImmutableArray<number> {
    return ImmutableArray.from<number>(source);
  }

  static arrayFresh(): ImmutableArray<number> {
    return ImmutableArray.of<number>([1, 2, 3, 4, 5]);
  }

  static arrayRange(source: number[]): ImmutableArray<number> {
    return ImmutableArray.fromRange<number>(source, 1, 2);
  }

  static arrayExpression(): ImmutableArray<number> {
    return ImmutableArray.of<number>([1, 2]);
  }

  static arrayEdited(array: ImmutableArray<number>, more: number[]): ImmutableArray<number> {
    return array.add(1).addRange(more).addRange([2, 3]);
  }

  static arrayReads(array: ImmutableArray<number>): string {
    return `${array.count}:${array.get(0)}:${array.fromEnd(1)}:${[...array.slice(1, array.count - 1)].join(",")}:${formatBoolean(array.isEmpty)}`;
  }

  static arrayBuilt(): ImmutableArray<number> {
    const builder = new ImmutableArrayBuilder<number>();
    builder.add(1);

    return toImmutableArray(builder);
  }

  static set(comparer: IEqualityComparer<string>): ImmutableHashSet<string> {
    return ImmutableHashSet.of<string>(["a", "b"], comparer);
  }

  static setRange(items: Iterable<number>): ImmutableHashSet<number> {
    return ImmutableHashSet.from<number>(items);
  }

  static setEdited(set: ImmutableHashSet<number>): ImmutableHashSet<number> {
    return set.add(1).remove(2).union([3]).except([4]);
  }

  static setFound(set: ImmutableHashSet<number>): boolean {
    const [$found0, actual] = set.tryGetValue(1, 0);

    return $found0 && actual === 1;
  }

  static dictionary(): ImmutableDictionary<string, number> {
    return (ImmutableDictionary.empty<string, number>()).add("a", 1).setItem("b", 2).remove("c");
  }

  static dictionaryExpression(source: Dictionary<string, number>): ImmutableDictionary<string, number> {
    return ImmutableDictionary.empty<string, number>().setItems([...source, ["k", 0] as [string, number]]);
  }

  static selected(words: List<string>): ImmutableDictionary<string, number> {
    return ImmutableDictionary.fromSelectors(words, (w: string) => w, (w: string) => w.length);
  }

  static lookup(map: ImmutableDictionary<string, number>): number {
    const [$found0, value] = map.tryGetValue("a", 0);

    return $found0 ? (value + map.getValueOrDefault("b", 0) + map.get("a")) | 0 : map.count;
  }

  static dictionaryBuilt(map: ImmutableDictionary<string, number>): ImmutableDictionary<string, number> {
    const builder = new ImmutableDictionaryBuilder(map);
    builder.set("z", 26);

    return toImmutableDictionary(builder);
  }

  static asInterface(list: ImmutableList<number>): IImmutableList<number> {
    return list.add(1);
  }
}
