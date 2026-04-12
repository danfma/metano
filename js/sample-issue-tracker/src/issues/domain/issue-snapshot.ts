import { HashCode } from "metano-runtime";
import { Temporal } from "@js-temporal/polyfill";
import { Decimal } from "decimal.js";
import type { UserId } from "#/shared-kernel";
import type { Comment } from "./comment";
import type { IssueId } from "./issue-id";
import { IssuePriority } from "./issue-priority";
import { IssueStatus } from "./issue-status";
import { IssueType } from "./issue-type";

export class IssueSnapshot {
  constructor(readonly id: IssueId, readonly title: string, readonly description: string, readonly type: IssueType, readonly priority: IssuePriority, readonly status: IssueStatus, readonly assigneeId: UserId | null, readonly sprintKey: string | null, readonly createdAt: Temporal.ZonedDateTime, readonly updatedAt: Temporal.ZonedDateTime, readonly estimatedHours: Decimal, readonly comments: Comment[]) { }

  equals(other: any): boolean {
    return other instanceof IssueSnapshot && this.id === other.id && this.title === other.title && this.description === other.description && this.type === other.type && this.priority === other.priority && this.status === other.status && this.assigneeId === other.assigneeId && this.sprintKey === other.sprintKey && this.createdAt === other.createdAt && this.updatedAt === other.updatedAt && this.estimatedHours === other.estimatedHours && this.comments === other.comments;
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
    return new IssueSnapshot(overrides?.id ?? this.id, overrides?.title ?? this.title, overrides?.description ?? this.description, overrides?.type ?? this.type, overrides?.priority ?? this.priority, overrides?.status ?? this.status, overrides?.assigneeId ?? this.assigneeId, overrides?.sprintKey ?? this.sprintKey, overrides?.createdAt ?? this.createdAt, overrides?.updatedAt ?? this.updatedAt, overrides?.estimatedHours ?? this.estimatedHours, overrides?.comments ?? this.comments);
  }
}
