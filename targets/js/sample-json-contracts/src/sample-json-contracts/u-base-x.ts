/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { UBaseH } from "./u-base-h";

export class UBaseX extends UBaseH {
  constructor(
    a: number,
    readonly x: number,
  ) {
    super(a);
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

  override with(overrides?: Partial<UBaseX>): UBaseX {
    return copyRecordWith<UBaseX>(this, overrides);
  }
}
