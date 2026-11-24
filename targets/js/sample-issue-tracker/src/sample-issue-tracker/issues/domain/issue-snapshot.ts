/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { Temporal } from "@js-temporal/polyfill";
import { HashCode, copyRecordWith, formatText, valueEquals, type ClassType } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";
import type { UserId } from "#/sample-issue-tracker/shared-kernel/user-id";
import type { Comment } from "./comment";
import type { IssueId } from "./issue-id";
import type { IssuePriority } from "./issue-priority";
import type { IssueStatus } from "./issue-status";
import type { IssueType } from "./issue-type";

let IssueSnapshot$rt: ClassType<IssueSnapshot> | undefined;

export class IssueSnapshot {
  constructor(
    readonly id: IssueId,
    readonly title: string,
    readonly description: string,
    readonly type: IssueType,
    readonly priority: IssuePriority,
    readonly status: IssueStatus,
    readonly assigneeId: UserId | null,
    readonly sprintKey: string | null,
    readonly createdAt: Temporal.ZonedDateTime,
    readonly updatedAt: Temporal.ZonedDateTime,
    readonly estimatedHours: Decimal,
    readonly comments: Comment[],
  ) {}

  static get $runtimeType(): ClassType<IssueSnapshot> {
    return (IssueSnapshot$rt ??= {
      kind: "class",
      name: "IssueSnapshot",
      fullName: "SampleIssueTracker.Issues.Domain.IssueSnapshot",
      namespace: "SampleIssueTracker.Issues.Domain",
      ctor: IssueSnapshot,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IssueSnapshot => v instanceof IssueSnapshot,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.title === other.title &&
      this.description === other.description &&
      this.type === other.type &&
      this.priority === other.priority &&
      this.status === other.status &&
      this.assigneeId === other.assigneeId &&
      this.sprintKey === other.sprintKey &&
      valueEquals(this.createdAt, other.createdAt) &&
      valueEquals(this.updatedAt, other.updatedAt) &&
      valueEquals(this.estimatedHours, other.estimatedHours) &&
      this.comments === other.comments
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.title);
    hc.add(this.description);
    hc.add(this.type);
    hc.add(this.priority);
    hc.add(this.status);
    hc.add(this.assigneeId);
    hc.add(this.sprintKey);
    hc.add(this.createdAt);
    hc.add(this.updatedAt);
    hc.add(this.estimatedHours);
    hc.add(this.comments);

    return hc.toHashCode();
  }

  with(overrides?: Partial<IssueSnapshot>): IssueSnapshot {
    return copyRecordWith<IssueSnapshot>(this, overrides);
  }

  protected $printMembers(): string {
    return `Id = ${this.id}, Title = ${this.title ?? ""}, Description = ${this.description ?? ""}, Type = ${this.type}, Priority = ${this.priority}, Status = ${this.status}, AssigneeId = ${this.assigneeId ?? ""}, SprintKey = ${this.sprintKey ?? ""}, CreatedAt = ${this.createdAt}, UpdatedAt = ${this.updatedAt}, EstimatedHours = ${formatText(this.estimatedHours)}, Comments = ${this.comments ?? ""}`;
  }

  toString(): string {
    return `IssueSnapshot { ${this.$printMembers()} }`;
  }
}
