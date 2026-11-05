/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class HolderInt {
  constructor(readonly n: number) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.n === other.n
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.n);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderInt>): HolderInt {
    return copyRecordWith<HolderInt>(this, overrides);
  }
}
