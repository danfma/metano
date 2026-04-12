import { HashCode } from "metano-runtime";

export class Pair<K, V> {
  constructor(readonly key: K, readonly value: V) { }

  equals(other: any): boolean {
    return other instanceof Pair && this.key === other.key && this.value === other.value;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.key);
    hc.add(this.value);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Pair<K, V>>): Pair<K, V> {
    return new Pair(overrides?.key ?? this.key, overrides?.value ?? this.value);
  }
}
