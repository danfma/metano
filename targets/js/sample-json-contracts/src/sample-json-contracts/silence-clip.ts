/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { Clip } from "./clip";

export class SilenceClip extends Clip {
  constructor(seconds: number) {
    super(seconds);
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<SilenceClip>): SilenceClip {
    return copyRecordWith<SilenceClip>(this, overrides);
  }
}
