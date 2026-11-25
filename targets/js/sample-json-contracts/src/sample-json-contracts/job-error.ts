/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { JobFinished } from "./job-finished.js";

let JobError$rt: ClassType<JobError> | undefined;

export class JobError extends JobFinished {
  constructor(readonly error: string) {
    super();
  }

  static override get $runtimeType(): ClassType<JobError> {
    return (JobError$rt ??= {
      kind: "class",
      name: "JobFailed",
      fullName: "SampleJsonContracts.JobFailed",
      namespace: "SampleJsonContracts",
      ctor: JobError,
      base: () => JobFinished.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is JobError => v instanceof JobError,
    });
  }

  override get kind(): "JobError" {
    return "JobError";
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.error === other.error;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.error);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<JobError>): JobError {
    return copyRecordWith<JobError>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Error = ${this.error}`;
  }

  override toString(): string {
    return `JobFailed { ${this.$printMembers()} }`;
  }
}
