/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export abstract class UNear {
  constructor(readonly a: number) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.a === other.a
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);

    return hc.toHashCode();
  }

  with(overrides?: Partial<UNear>): UNear {
    return copyRecordWith<UNear>(this, overrides);
  }
}
