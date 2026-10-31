/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class Counter {
  static readonly zero: Counter = new Counter(0);

  constructor(readonly count: number) { }

  increment(): Counter {
    return this.with({ count: this.count + 1 });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.count === other.count
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.count);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Counter>): Counter {
    return copyRecordWith<Counter>(this, overrides);
  }
}
