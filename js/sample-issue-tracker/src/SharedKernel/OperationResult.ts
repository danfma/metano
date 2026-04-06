import { HashCode } from "@meta-sharp/runtime";
export class OperationResult<T> {
  constructor(readonly success: boolean, readonly value: T | null, readonly errorCode: string | null = null, readonly errorMessage: string | null = null) { }

  get hasValue(): boolean {
    return this.success && !(this.value === null);
  }

  static ok<T>(value: T): OperationResult<T> {
    return new OperationResult(true, value);
  }

  static fail<T>(errorCode: string, errorMessage: string): OperationResult<T> {
    return new OperationResult(false, undefined, errorCode, errorMessage);
  }

  equals(other: any): boolean {
    return other instanceof OperationResult && this.success === other.success && this.value === other.value && this.errorCode === other.errorCode && this.errorMessage === other.errorMessage;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.success);
    hc.add(this.value);
    hc.add(this.errorCode);
    hc.add(this.errorMessage);
    return hc.toHashCode();
  }

  with(overrides?: Partial<OperationResult<T>>): OperationResult<T> {
    return new OperationResult(overrides?.success ?? this.success, overrides?.value ?? this.value, overrides?.errorCode ?? this.errorCode, overrides?.errorMessage ?? this.errorMessage);
  }
}
