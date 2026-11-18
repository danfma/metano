/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { HashCode, copyRecordWith, defaultEquals, type ClassType } from "metano-runtime";

let OperationResult$rt: ClassType<OperationResult<unknown>> | undefined;

export class OperationResult<T> {
  constructor(
    readonly success: boolean,
    readonly value: T | null,
    readonly errorCode: string | null = null,
    readonly errorMessage: string | null = null,
  ) {}

  static get $runtimeType(): ClassType<OperationResult<unknown>> {
    return (OperationResult$rt ??= {
      kind: "class",
      name: "OperationResult`1",
      fullName: "SampleIssueTracker.SharedKernel.OperationResult`1",
      namespace: "SampleIssueTracker.SharedKernel",
      ctor: OperationResult,
      interfaces: [],
      typeParameters: ["T"],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is OperationResult<unknown> => v instanceof OperationResult,
    });
  }

  get hasValue(): boolean {
    return this.success && !(this.value == null);
  }

  static ok<T>(value: T): OperationResult<T> {
    return new OperationResult(true, value);
  }

  static fail<T>(errorCode: string, errorMessage: string): OperationResult<T> {
    return new OperationResult(false, null, errorCode, errorMessage);
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.success === other.success &&
      defaultEquals(this.value, other.value) &&
      this.errorCode === other.errorCode &&
      this.errorMessage === other.errorMessage
    );
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
    return copyRecordWith<OperationResult<T>>(this, overrides);
  }
}
