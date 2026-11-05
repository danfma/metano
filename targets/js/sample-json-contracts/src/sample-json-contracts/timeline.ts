/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { Clip } from "./clip";

export class Timeline {
  constructor(
    readonly name: string,
    readonly main: Clip,
    readonly clips: Clip[],
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.name === other.name &&
      valueEquals(this.main, other.main) &&
      this.clips === other.clips
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.name);
    hc.add(this.main);
    hc.add(this.clips);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Timeline>): Timeline {
    return copyRecordWith<Timeline>(this, overrides);
  }
}
