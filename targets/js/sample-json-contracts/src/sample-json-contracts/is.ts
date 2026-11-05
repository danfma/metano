/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { IBase } from "./i-base";

export class IS extends IBase {
  constructor(readonly y: number) {
    super();
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.y === other.y;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.y);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<IS>): IS {
    return copyRecordWith<IS>(this, overrides);
  }
}
