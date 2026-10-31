/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Temporal } from "@js-temporal/polyfill";
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { Decimal } from "decimal.js";
import type { UserId } from "#/sample-issue-tracker/shared-kernel/user-id";
import type { Comment } from "./comment";
import type { IssueId } from "./issue-id";
import type { IssuePriority } from "./issue-priority";
import type { IssueStatus } from "./issue-status";
import type { IssueType } from "./issue-type";

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
}
