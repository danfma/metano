/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class Int64Box {
  constructor(
    readonly id: number,
    readonly mask: number,
    readonly parent: number | null,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.mask === other.mask &&
      this.parent === other.parent
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.mask);
    hc.add(this.parent);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Int64Box>): Int64Box {
    return copyRecordWith<Int64Box>(this, overrides);
  }
}
