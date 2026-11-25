/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { JobStatus } from "./job-status.js";

let JobRunning$rt: ClassType<JobRunning> | undefined;

export class JobRunning extends JobStatus {
  declare readonly percent: number;

  constructor(percent: number) {
    super();
    this.percent = percent;
  }

  static override get $runtimeType(): ClassType<JobRunning> {
    return (JobRunning$rt ??= {
      kind: "class",
      name: "JobRunning",
      fullName: "SampleJsonContracts.JobRunning",
      namespace: "SampleJsonContracts",
      ctor: JobRunning,
      base: () => JobStatus.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is JobRunning => v instanceof JobRunning,
    });
  }

  override get kind(): "JobRunning" {
    return "JobRunning";
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.percent === other.percent;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.percent);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<JobRunning>): JobRunning {
    return copyRecordWith<JobRunning>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Percent = ${this.percent}`;
  }

  override toString(): string {
    return `JobRunning { ${this.$printMembers()} }`;
  }
}

export function isJobRunning(value: unknown): value is JobRunning {
  return value instanceof JobRunning;
}

export function assertJobRunning(value: unknown, message?: string): asserts value is JobRunning {
  if (!isJobRunning(value)) {
    throw new TypeError(message ?? "Value is not a JobRunning");
  }
}
