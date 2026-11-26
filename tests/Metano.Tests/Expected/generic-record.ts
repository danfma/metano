import { HashCode, copyRecordWith, defaultEquals, formatBoolean, objectText } from "metano-runtime";

export class Result<T> {
  declare readonly value: T;
  declare readonly success: boolean;

  constructor(value: T, success: boolean) {
    this.value = value;
    this.success = success;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && defaultEquals(this.value, other.value) && this.success === other.success;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);
    hc.add(this.success);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Result<T>>): Result<T> {
    return copyRecordWith<Result<T>>(this, overrides);
  }

  protected $printMembers(): string {
    return `Value = ${objectText(this.value ?? "")}, Success = ${formatBoolean(this.success)}`;
  }

  toString(): string {
    return `Result { ${this.$printMembers()} }`;
  }
}
