/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { NBase } from "./n-base";

export class NMid extends NBase {
  constructor(
    a: number,
    readonly b: number,
  ) {
    super(a);
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.b === other.b;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.b);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<NMid>): NMid {
    return copyRecordWith<NMid>(this, overrides);
  }
}
