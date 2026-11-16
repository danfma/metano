import { HashCode, copyRecordWith, defaultEquals } from "metano-runtime";

export class Pair<K, V> {
  constructor(readonly key: K, readonly value: V) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && defaultEquals(this.key, other.key) && defaultEquals(this.value, other.value);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.key);
    hc.add(this.value);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Pair<K, V>>): Pair<K, V> {
    return copyRecordWith<Pair<K, V>>(this, overrides);
  }
}
