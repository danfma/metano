/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, type UUID, copyRecordWith } from "metano-runtime";

export class GuidMapHolder {
  constructor(readonly byId: Map<UUID, string>) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.byId === other.byId
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.byId);

    return hc.toHashCode();
  }

  with(overrides?: Partial<GuidMapHolder>): GuidMapHolder {
    return copyRecordWith<GuidMapHolder>(this, overrides);
  }
}
