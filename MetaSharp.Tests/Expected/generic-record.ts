import { HashCode } from "@meta-sharp/runtime";
export class Result<T> {
  constructor(readonly value: T, readonly success: boolean) { }

  equals(other: any): boolean {
    return other instanceof Result && this.value === other.value && this.success === other.success;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);
    hc.add(this.success);
    return hc.toHashCode();
  }

  with(overrides?: Partial<Result<T>>): Result<T> {
    return new Result(overrides?.value ?? this.value, overrides?.success ?? this.success);
  }
}
