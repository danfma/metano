/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { UNear } from "./u-near";

export abstract class UNearAbs extends UNear {
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

  override with(overrides?: Partial<UNearAbs>): UNearAbs {
    return copyRecordWith<UNearAbs>(this, overrides);
  }
}
