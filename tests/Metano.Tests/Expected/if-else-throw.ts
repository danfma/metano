import { HashCode, copyRecordWith } from "metano-runtime";

export class Pair {
  constructor(readonly a: number, readonly b: number) { }

  static check(pair: Pair): void {
    if (pair.a !== pair.b) {
      throw new Error("mismatch");
    }
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.a === other.a && this.b === other.b;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);
    hc.add(this.b);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Pair>): Pair {
    return copyRecordWith<Pair>(this, overrides);
  }
}
