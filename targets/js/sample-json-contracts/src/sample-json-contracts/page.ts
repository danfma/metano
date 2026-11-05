/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class Page<T> {
  constructor(readonly items: T[]) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.items === other.items
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.items);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Page<T>>): Page<T> {
    return copyRecordWith<Page<T>>(this, overrides);
  }
}
