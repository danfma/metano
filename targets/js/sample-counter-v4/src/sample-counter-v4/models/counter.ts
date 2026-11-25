/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let Counter$rt: ClassType<Counter> | undefined;

export class Counter {
  static readonly zero = /* @__PURE__ */ new Counter(0) as Counter;

  constructor(readonly count: number) {}

  static get $runtimeType(): ClassType<Counter> {
    return (Counter$rt ??= {
      kind: "class",
      name: "Counter",
      fullName: "SampleCounterV4.Models.Counter",
      namespace: "SampleCounterV4.Models",
      ctor: Counter,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Counter => v instanceof Counter,
    });
  }

  increment(): Counter {
    return this.with({ count: (this.count + 1) | 0 });
  }

  decrement(): Counter {
    return this.with({ count: (this.count - 1) | 0 });
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
