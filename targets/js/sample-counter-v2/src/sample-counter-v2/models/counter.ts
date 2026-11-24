/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class Counter {
  static readonly zero = /* @__PURE__ */ new Counter(0) as Counter;

  constructor(readonly count: number) {}

  increment(): Counter {
    return this.with({ count: (this.count + 1) | 0 });
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

  toString(): string {
    return `Counter { Count = ${this.count} }`;
  }
}
