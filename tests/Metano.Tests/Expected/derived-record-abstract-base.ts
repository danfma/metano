import { HashCode, copyRecordWith } from "metano-runtime";

export abstract class Clip {
  constructor(readonly seconds: number) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.seconds === other.seconds;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.seconds);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Clip>): Clip {
    return copyRecordWith<Clip>(this, overrides);
  }
}
