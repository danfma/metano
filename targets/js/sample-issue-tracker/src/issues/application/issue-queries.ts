/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Grouping } from "metano-runtime";
import {
  count,
  groupBy,
  linq,
  orderBy,
  orderByDescending,
  take,
  thenBy,
  thenByDescending,
  toArray,
  toMap,
  where,
} from "metano-runtime/system/linq";
import { IssuePriority, IssueStatus, type Issue } from "#/issues/domain";
import type { UserId } from "#/shared-kernel";

export function openIssues(issues: Issue[]): Issue[] {
  return linq(
    issues,
    where((issue: Issue) => !issue.isClosed),
    orderByDescending((issue: Issue) => issue.priority),
    thenBy((issue: Issue) => issue.title),
    toArray(),
  );
}

export function statusCounts(issues: Issue[]): Map<IssueStatus, number> {
  return linq(
    issues,
    groupBy((issue: Issue) => issue.status),
    toMap(
      (group: Grouping<IssueStatus, Issue>) => group.key,
      (group: Grouping<IssueStatus, Issue>) => linq(group, count()),
    ),
  );
}

export function issuesForAssignee(issues: Issue[], assigneeId: UserId): Issue[] {
  return linq(
    issues,
    where((issue: Issue) => issue.assigneeId === assigneeId),
    orderBy((issue: Issue) => issue.status),
    thenByDescending((issue: Issue) => issue.priority),
    toArray(),
  );
}

export function readyForReview(issues: Issue[], limit: number): Issue[] {
  return linq(
    issues,
    where(
      (issue: Issue) =>
        ((issue: Issue) =>
          issue.status === IssueStatus.InProgress || issue.status === IssueStatus.InReview)(
          issue,
        ) &&
        ((issue: Issue) =>
          issue.priority === IssuePriority.High || issue.priority === IssuePriority.Urgent)(issue),
    ),
    take(limit),
    toArray(),
  );
}
