import { Enumerable } from "metano-runtime";
import type { Grouping } from "metano-runtime";
import type { Issue } from "#/issues/domain";
import { IssuePriority } from "#/issues/domain";
import { IssueStatus } from "#/issues/domain";
import type { UserId } from "#/shared-kernel";

export function openIssues(issues: Issue[]): Issue[] {
  return Enumerable.from(issues).where((issue: Issue) => !issue.isClosed).orderByDescending((issue: Issue) => issue.priority).thenBy((issue: Issue) => issue.title).toArray();
}

export function statusCounts(issues: Issue[]): Map<IssueStatus, number> {
  return Enumerable.from(issues).groupBy((issue: Issue) => issue.status).toMap((group: Grouping<IssueStatus, Issue>) => group.key, (group: Grouping<IssueStatus, Issue>) => Enumerable.from(group).count());
}

export function issuesForAssignee(issues: Issue[], assigneeId: UserId): Issue[] {
  return Enumerable.from(issues).where((issue: Issue) => issue.assigneeId === assigneeId).orderBy((issue: Issue) => issue.status).thenByDescending((issue: Issue) => issue.priority).toArray();
}

export function readyForReview(issues: Issue[], limit: number): Issue[] {
  return Enumerable.from(issues).where((issue: Issue) => issue.status === IssueStatus.InProgress || issue.status === IssueStatus.InReview).where((issue: Issue) => issue.priority === IssuePriority.High || issue.priority === IssuePriority.Urgent).take(limit).toArray();
}
