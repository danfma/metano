/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { MixedBase } from "./mixed-base";

export class MixedClass extends MixedBase {
  constructor(readonly w: number) {
    super();
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

  override with(overrides?: Partial<MixedClass>): MixedClass {
    return copyRecordWith<MixedClass>(this, overrides);
  }
}
