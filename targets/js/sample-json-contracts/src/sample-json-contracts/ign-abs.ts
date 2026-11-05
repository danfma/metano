/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export abstract class IgnAbs {
  constructor(readonly v: number) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.v === other.v
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  with(overrides?: Partial<IgnAbs>): IgnAbs {
    return copyRecordWith<IgnAbs>(this, overrides);
  }
}
