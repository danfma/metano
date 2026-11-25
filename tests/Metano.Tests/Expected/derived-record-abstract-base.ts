import { HashCode, copyRecordWith, formatDouble, valueEquals } from "metano-runtime";

export abstract class Clip {
  constructor(readonly seconds: number) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.seconds, other.seconds);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.seconds);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Clip>): Clip {
    return copyRecordWith<Clip>(this, overrides);
  }

  protected $printMembers(): string {
    return `Seconds = ${formatDouble(this.seconds)}`;
  }

  toString(): string {
    return `Clip { ${this.$printMembers()} }`;
  }
}
