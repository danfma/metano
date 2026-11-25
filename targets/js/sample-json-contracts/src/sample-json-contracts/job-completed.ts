/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  formatDouble,
  valueEquals,
  type ClassType,
} from "metano-runtime";
import { JobFinished } from "./job-finished.js";

let JobCompleted$rt: ClassType<JobCompleted> | undefined;

export class JobCompleted extends JobFinished {
  constructor(readonly seconds: number) {
    super();
  }

  static override get $runtimeType(): ClassType<JobCompleted> {
    return (JobCompleted$rt ??= {
      kind: "class",
      name: "JobCompleted",
      fullName: "SampleJsonContracts.JobCompleted",
      namespace: "SampleJsonContracts",
      ctor: JobCompleted,
      base: () => JobFinished.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is JobCompleted => v instanceof JobCompleted,
    });
  }

  override get kind(): "JobCompleted" {
    return "JobCompleted";
  }

  override equals(other: any): boolean {
    return super.equals(other) && valueEquals(this.seconds, other.seconds);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.seconds);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<JobCompleted>): JobCompleted {
    return copyRecordWith<JobCompleted>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Seconds = ${formatDouble(this.seconds)}`;
  }

  override toString(): string {
    return `JobCompleted { ${this.$printMembers()} }`;
  }
}
