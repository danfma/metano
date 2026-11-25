/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { JobCompleted } from "./job-completed.js";
import type { JobError } from "./job-error.js";
import { JobStatus } from "./job-status.js";

let JobFinished$rt: ClassType<JobFinished$Base> | undefined;

abstract class JobFinished$Base extends JobStatus {
  abstract override readonly kind: "JobCompleted" | "JobError";

  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<JobFinished$Base> {
    return (JobFinished$rt ??= {
      kind: "class",
      name: "JobFinished",
      fullName: "SampleJsonContracts.JobFinished",
      namespace: "SampleJsonContracts",
      ctor: JobFinished$Base,
      base: () => JobStatus.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is JobFinished$Base => v instanceof JobFinished$Base,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<JobFinished>): JobFinished {
    return copyRecordWith<JobFinished>(this as unknown as JobFinished, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `JobFinished { ${members} }` : "JobFinished { }";
  }
}

export type JobFinished = JobCompleted | JobError;

export const JobFinished: typeof JobFinished$Base = JobFinished$Base;
