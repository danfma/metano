/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { UNearA } from "./u-near-a";

export class UNearB extends UNearA {
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

  override with(overrides?: Partial<UNearB>): UNearB {
    return copyRecordWith<UNearB>(this, overrides);
  }
}
