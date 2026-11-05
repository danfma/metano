/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { NMid } from "./n-mid";

export class NLeaf extends NMid {
  constructor(
    a: number,
    b: number,
    readonly c: number,
  ) {
    super(a, b);
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.c === other.c;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.c);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<NLeaf>): NLeaf {
    return copyRecordWith<NLeaf>(this, overrides);
  }
}
