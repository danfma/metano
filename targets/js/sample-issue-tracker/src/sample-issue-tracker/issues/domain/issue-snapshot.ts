/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { Temporal } from "@js-temporal/polyfill";
import { HashCode, copyRecordWith, formatText, valueEquals, type ClassType } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";
import type { UserId } from "../../shared-kernel/user-id.js";
import type { Comment } from "./comment.js";
import type { IssueId } from "./issue-id.js";
import type { IssuePriority } from "./issue-priority.js";
import type { IssueStatus } from "./issue-status.js";
import type { IssueType } from "./issue-type.js";

let IssueSnapshot$rt: ClassType<IssueSnapshot> | undefined;

export class IssueSnapshot {
  declare readonly id: IssueId;
  declare readonly title: string;
  declare readonly description: string;
  declare readonly type: IssueType;
  declare readonly priority: IssuePriority;
  declare readonly status: IssueStatus;
  declare readonly assigneeId: UserId | null;
  declare readonly sprintKey: string | null;
  declare readonly createdAt: Temporal.ZonedDateTime;
  declare readonly updatedAt: Temporal.ZonedDateTime;
  declare readonly estimatedHours: Decimal;
  declare readonly comments: Comment[];

  constructor(
    id: IssueId,
    title: string,
    description: string,
    type: IssueType,
    priority: IssuePriority,
    status: IssueStatus,
    assigneeId: UserId | null,
    sprintKey: string | null,
    createdAt: Temporal.ZonedDateTime,
    updatedAt: Temporal.ZonedDateTime,
    estimatedHours: Decimal,
    comments: Comment[],
  ) {
    this.id = id;
    this.title = title;
    this.description = description;
    this.type = type;
    this.priority = priority;
    this.status = status;
    this.assigneeId = assigneeId;
    this.sprintKey = sprintKey;
    this.createdAt = createdAt;
    this.updatedAt = updatedAt;
    this.estimatedHours = estimatedHours;
    this.comments = comments;
  }

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
