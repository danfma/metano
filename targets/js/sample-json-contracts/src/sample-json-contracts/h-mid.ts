/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { HBase } from "./h-base";

export class HMid extends HBase {
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

  override with(overrides?: Partial<HMid>): HMid {
    return copyRecordWith<HMid>(this, overrides);
  }
}
