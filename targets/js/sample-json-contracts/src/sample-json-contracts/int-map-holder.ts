/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class IntMapHolder {
  constructor(readonly byKey: Map<number, string>) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.byKey === other.byKey
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.byKey);

    return hc.toHashCode();
  }

  with(overrides?: Partial<IntMapHolder>): IntMapHolder {
    return copyRecordWith<IntMapHolder>(this, overrides);
  }
}
