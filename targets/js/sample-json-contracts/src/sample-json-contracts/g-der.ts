/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { GBase } from "./g-base";

export class GDer extends GBase<number> {
  constructor(readonly extra: number) {
    super(1);
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.extra === other.extra;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.extra);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<GDer>): GDer {
    return copyRecordWith<GDer>(this, overrides);
  }
}
