/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { Temporal } from "@js-temporal/polyfill";
import { HashCode, copyRecordWith, durationFromTicks, type ClassType } from "metano-runtime";
import type { SnowflakeId } from "../../shared-kernel/snowflake-id.js";
import type { IssueActivityKind } from "./issue-activity-kind.js";

let IssueActivity$rt: ClassType<IssueActivity> | undefined;

export class IssueActivity {
  constructor(
    readonly id: SnowflakeId,
    readonly issueKey: string,
    readonly kind: IssueActivityKind,
    readonly occurredAtTicks: bigint,
    readonly durationTicks: bigint | null,
  ) {}

  static get $runtimeType(): ClassType<IssueActivity> {
    return (IssueActivity$rt ??= {
      kind: "class",
      name: "IssueActivity",
      fullName: "SampleIssueTracker.Issues.Domain.IssueActivity",
      namespace: "SampleIssueTracker.Issues.Domain",
      ctor: IssueActivity,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IssueActivity => v instanceof IssueActivity,
    });
  }

  elapsedTicksUntil(endTicks: bigint): bigint {
    return BigInt.asIntN(64, endTicks - this.occurredAtTicks);
  }

  occurredWithin(startTicks: bigint, endTicks: bigint): boolean {
    return this.occurredAtTicks >= startTicks && this.occurredAtTicks <= endTicks;
  }

  duration(): Temporal.Duration {
    return durationFromTicks(this.durationTicks ?? 0n);
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.issueKey === other.issueKey &&
      this.kind === other.kind &&
      this.occurredAtTicks === other.occurredAtTicks &&
      this.durationTicks === other.durationTicks
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.issueKey);
    hc.add(this.kind);
    hc.add(this.occurredAtTicks);
    hc.add(this.durationTicks);

    return hc.toHashCode();
  }

  with(overrides?: Partial<IssueActivity>): IssueActivity {
    return copyRecordWith<IssueActivity>(this, overrides);
  }
}
