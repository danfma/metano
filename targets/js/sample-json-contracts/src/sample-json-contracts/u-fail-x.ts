/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { UFail } from "./u-fail";

export class UFailX extends UFail {
  constructor(readonly a: number) {
    super();
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.a === other.a;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.a);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<UFailX>): UFailX {
    return copyRecordWith<UFailX>(this, overrides);
  }
}
