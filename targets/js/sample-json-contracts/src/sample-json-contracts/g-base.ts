/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export abstract class GBase<T> {
  constructor(readonly value: T) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.value === other.value
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);

    return hc.toHashCode();
  }

  with(overrides?: Partial<GBase<T>>): GBase<T> {
    return copyRecordWith<GBase<T>>(this, overrides);
  }
}
