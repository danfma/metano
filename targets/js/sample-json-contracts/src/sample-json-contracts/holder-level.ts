/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import type { Level } from "./level";

export class HolderLevel {
  constructor(readonly level: Level) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.level === other.level
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.level);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderLevel>): HolderLevel {
    return copyRecordWith<HolderLevel>(this, overrides);
  }
}
