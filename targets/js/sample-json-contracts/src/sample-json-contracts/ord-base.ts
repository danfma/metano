/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class OrdBase {
  constructor(
    readonly second: number,
    readonly first: number,
    readonly third: number,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.second === other.second &&
      this.first === other.first &&
      this.third === other.third
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.second);
    hc.add(this.first);
    hc.add(this.third);

    return hc.toHashCode();
  }

  with(overrides?: Partial<OrdBase>): OrdBase {
    return copyRecordWith<OrdBase>(this, overrides);
  }
}
