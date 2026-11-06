/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { SPoint } from "./s-point";

export class HolderSPoint {
  constructor(readonly s: SPoint) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.s, other.s)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.s);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderSPoint>): HolderSPoint {
    return copyRecordWith<HolderSPoint>(this, overrides);
  }
}
