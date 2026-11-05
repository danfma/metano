/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { IgnBase } from "./ign-base";

export class IgnDer extends IgnBase {
  constructor(
    v: number,
    readonly w: number,
  ) {
    super(v);
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.w === other.w;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.w);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<IgnDer>): IgnDer {
    return copyRecordWith<IgnDer>(this, overrides);
  }
}
