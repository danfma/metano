import { ConcurrentDictionary } from "metano-runtime/system/collections";
import { formatBoolean } from "metano-runtime";

export class Caches {
  private constructor() { }

  static build(): ConcurrentDictionary<string, number> {
    return ConcurrentDictionary.assign<string, number>([
      ["a", 1],
    ]);
  }

  static none(): ConcurrentDictionary<string, number> {
    return new ConcurrentDictionary<string, number>();
  }

  static copy(source: Iterable<readonly [string, number]>): ConcurrentDictionary<string, number> {
    return ConcurrentDictionary.from(source);
  }

  static sized(): ConcurrentDictionary<string, number> {
    return ConcurrentDictionary.withCapacity<string, number>(4, 16);
  }

  static use(cache: ConcurrentDictionary<string, number>): string {
    const a = cache.getOrAdd("a", 1);
    const b = cache.getOrAddWith("b", (key: string) => key.length);
    const c = cache.getOrAddWith("c", (key: string, extra: number) => (key.length + extra) | 0, 10);
    const d = cache.addOrUpdate("a", 0, (key: string, old: number) => (old + 1) | 0);
    const e = cache.addOrUpdateWith("e", (key: string) => 5, (key: string, old: number) => Math.imul(old, 2));
    cache.set("f", 6);
    cache.set("f", (cache.get("f") + 1) | 0);
    const added = cache.tryAdd("g", 7);
    const updated = cache.tryUpdate("g", 8, 7);
    const [removed, gone] = cache.tryRemove("a", 0);
    const [found, value] = cache.tryGetValue("b", 0);

    return `${a}${b}${c}${d}${e}${formatBoolean(added)}${formatBoolean(updated)}${formatBoolean(removed)}${gone}${formatBoolean(found)}${value}${cache.count}${formatBoolean(cache.isEmpty)}${cache.getValueOrDefault("zz", 0)}`;
  }
}
