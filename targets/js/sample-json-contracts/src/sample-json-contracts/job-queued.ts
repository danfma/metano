/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { JobStatus } from "./job-status.js";

let JobQueued$rt: ClassType<JobQueued> | undefined;

export class JobQueued extends JobStatus {
  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<JobQueued> {
    return (JobQueued$rt ??= {
      kind: "class",
      name: "JobQueued",
      fullName: "SampleJsonContracts.JobQueued",
      namespace: "SampleJsonContracts",
      ctor: JobQueued,
      base: () => JobStatus.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new JobQueued(),
      test: (v: unknown): v is JobQueued => v instanceof JobQueued,
    });
  }

  override get kind(): "JobQueued" {
    return "JobQueued";
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<JobQueued>): JobQueued {
    return copyRecordWith<JobQueued>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `JobQueued { ${members} }` : "JobQueued { }";
  }
}
