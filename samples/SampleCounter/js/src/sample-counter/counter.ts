import { HashCode } from "metano-runtime";

export class Counter {
  constructor(readonly count: number) { }

  static get zero(): Counter {
    return new Counter(0);
  }

  increment(): Counter {
    return new Counter(this.count + 1);
  }

  decrement(): Counter {
    return new Counter(this.count - 1);
  }

  equals(other: any): boolean {
    return other instanceof Counter && this.count === other.count;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.count);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Counter>): Counter {
    return new Counter(overrides?.count ?? this.count);
  }
}
