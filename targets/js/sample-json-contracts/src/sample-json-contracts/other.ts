/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import type { IShape } from "./i-shape";

export class Other implements IShape {
  constructor(readonly side: number) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.side === other.side
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.side);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Other>): Other {
    return copyRecordWith<Other>(this, overrides);
  }
}
