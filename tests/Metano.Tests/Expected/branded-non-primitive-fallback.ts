import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import { Inner } from "./inner";

export class Boxed {
  constructor(readonly value: Inner) { }

  static make(x: number): Boxed {
    return new Boxed(new Inner(x));
  }

  twice(): number {
    return Math.imul(this.value.x, 2);
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.value, other.value);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Boxed>): Boxed {
    return copyRecordWith<Boxed>(this, overrides);
  }

  toString(): string {
    return `Boxed { Value = ${this.value ?? ""} }`;
  }
}
