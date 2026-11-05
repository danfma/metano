/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { UFail } from "./u-fail";

export class UFailY extends UFail {
  constructor() {
    super();
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<UFailY>): UFailY {
    return copyRecordWith<UFailY>(this, overrides);
  }
}
