/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { Dictionary, IReadOnlyList } from "metano-runtime/system/collections";
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
  toDictionary,
  toList,
  where,
} from "metano-runtime/system/linq";
import type { UserId } from "../../shared-kernel/user-id.js";
import { IssuePriority } from "../domain/issue-priority.js";
import { IssueStatus } from "../domain/issue-status.js";
import type { Issue } from "../domain/issue.js";

export function openIssues(issues: IReadOnlyList<Issue>): IReadOnlyList<Issue> {
  return linq(
    issues,
    where((issue: Issue) => !issue.isClosed),
    orderByDescending((issue: Issue) => issue.priority),
    thenBy((issue: Issue) => issue.title),
    toList(),
  );
}

export function statusCounts(issues: IReadOnlyList<Issue>): Dictionary<IssueStatus, number> {
  return linq(
    issues,
    groupBy((issue: Issue) => issue.status),
    toDictionary(
      (group: Grouping<IssueStatus, Issue>) => group.key,
      (group: Grouping<IssueStatus, Issue>) => linq(group, count()),
    ),
  );
}

export function issuesForAssignee(
  issues: IReadOnlyList<Issue>,
  assigneeId: UserId,
): IReadOnlyList<Issue> {
  return linq(
    issues,
    where((issue: Issue) => issue.assigneeId === assigneeId),
    orderBy((issue: Issue) => issue.status),
    thenByDescending((issue: Issue) => issue.priority),
    toList(),
  );
}

export function readyForReview(issues: IReadOnlyList<Issue>, limit: number): IReadOnlyList<Issue> {
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
    toList(),
  );
}
