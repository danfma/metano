import { HashCode, copyRecordWith, defaultEquals, objectText } from "metano-runtime";

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

  protected $printMembers(): string {
    return `Key = ${objectText(this.key ?? "")}, Value = ${objectText(this.value ?? "")}`;
  }

  toString(): string {
    return `Pair { ${this.$printMembers()} }`;
  }
}
