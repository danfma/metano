/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class Stats {
  constructor(
    readonly count: number,
    readonly ratio: number,
    readonly userName: string,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.count === other.count &&
      this.ratio === other.ratio &&
      this.userName === other.userName
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.count);
    hc.add(this.ratio);
    hc.add(this.userName);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Stats>): Stats {
    return copyRecordWith<Stats>(this, overrides);
  }
}
