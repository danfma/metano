/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { DBase } from "./d-base";

export class DBer extends DBase {
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

  override with(overrides?: Partial<DBer>): DBer {
    return copyRecordWith<DBer>(this, overrides);
  }
}
