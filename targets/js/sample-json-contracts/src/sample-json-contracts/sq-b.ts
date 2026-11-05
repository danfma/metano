/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import type { IShapeB } from "./i-shape-b";

export class SqB implements IShapeB {
  constructor(
    readonly area: number,
    readonly side: number,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.area === other.area &&
      this.side === other.side
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.area);
    hc.add(this.side);

    return hc.toHashCode();
  }

  with(overrides?: Partial<SqB>): SqB {
    return copyRecordWith<SqB>(this, overrides);
  }
}
