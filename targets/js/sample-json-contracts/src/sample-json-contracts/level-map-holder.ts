/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import type { Level } from "./level";

export class LevelMapHolder {
  constructor(readonly byLevel: Map<Level, string>) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.byLevel === other.byLevel
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.byLevel);

    return hc.toHashCode();
  }

  with(overrides?: Partial<LevelMapHolder>): LevelMapHolder {
    return copyRecordWith<LevelMapHolder>(this, overrides);
  }
}
