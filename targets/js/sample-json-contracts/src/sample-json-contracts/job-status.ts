/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { JobFinished } from "./job-finished.js";
import type { JobQueued } from "./job-queued.js";
import type { JobRunning } from "./job-running.js";

let JobStatus$rt: ClassType<JobStatus$Base> | undefined;

abstract class JobStatus$Base {
  abstract readonly kind: "JobQueued" | "JobRunning" | "JobCompleted" | "JobError";

  static get $runtimeType(): ClassType<JobStatus$Base> {
    return (JobStatus$rt ??= {
      kind: "class",
      name: "JobStatus",
      fullName: "SampleJsonContracts.JobStatus",
      namespace: "SampleJsonContracts",
      ctor: JobStatus$Base,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is JobStatus$Base => v instanceof JobStatus$Base,
    });
  }

  static get family(): string {
    return "jobs";
  }

  self(): JobStatus {
    return this as unknown as JobStatus;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<JobStatus>): JobStatus {
    return copyRecordWith<JobStatus>(this as unknown as JobStatus, overrides);
  }

  protected $printMembers(): string {
    return "";
  }

  toString(): string {
    const members = this.$printMembers();

    return members ? `JobStatus { ${members} }` : "JobStatus { }";
  }
}

export type JobStatus = JobQueued | JobRunning | JobFinished;

export const JobStatus = JobStatus$Base as {
  [Symbol.hasInstance]: <$P>(
    this: { prototype: $P },
    value: unknown,
  ) => value is JobStatus$Base extends $P ? JobStatus : $P;
} & typeof JobStatus$Base;

export function isJobStatus(value: unknown): value is JobStatus {
  return value instanceof JobStatus$Base;
}

export function assertJobStatus(value: unknown, message?: string): asserts value is JobStatus {
  if (!isJobStatus(value)) {
    throw new TypeError(message ?? "Value is not a JobStatus");
  }
}
