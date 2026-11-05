/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import type { IShapeB } from "./i-shape-b";

export class OtherB implements IShapeB {
  constructor(readonly area: number) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.area === other.area
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.area);

    return hc.toHashCode();
  }

  with(overrides?: Partial<OtherB>): OtherB {
    return copyRecordWith<OtherB>(this, overrides);
  }
}
