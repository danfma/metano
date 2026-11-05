/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { UBaseH } from "./u-base-h";

export class UBaseA extends UBaseH {
  constructor(a: number) {
    super(a);
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<UBaseA>): UBaseA {
    return copyRecordWith<UBaseA>(this, overrides);
  }
}
