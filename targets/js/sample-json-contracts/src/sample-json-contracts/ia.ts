/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { IBase } from "./i-base";

export class IA extends IBase {
  constructor(readonly x: number) {
    super();
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.x === other.x;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.x);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<IA>): IA {
    return copyRecordWith<IA>(this, overrides);
  }
}
