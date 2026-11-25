import { HashCode, copyRecordWith } from "metano-runtime";
import { Base } from "./base.js";

export class Derived extends Base {
  constructor(name: string, readonly level: number) {
    super(name);
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.level === other.level;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.level);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Derived>): Derived {
    return copyRecordWith<Derived>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Level = ${this.level}`;
  }

  override toString(): string {
    return `Derived { ${this.$printMembers()} }`;
  }
}
