import { Issue } from "#/issues/domain/issue";
import { IssueId } from "#/issues/domain/issue-id";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueType } from "#/issues/domain/issue-type";
import { UserId } from "#/shared-kernel/user-id";

export function makeIssue(overrides?: Partial<{
  title: string;
  description: string;
  type: IssueType;
  priority: IssuePriority;
  status: IssueStatus;
  assigneeId: UserId;
}>): Issue {
  const issue = new Issue(
    IssueId.new_(),
    overrides?.title ?? "Test issue",
    overrides?.description ?? "Test description",
    overrides?.type ?? IssueType.Story,
    overrides?.priority ?? IssuePriority.Medium
  );
  if (overrides?.status !== undefined) issue.status = overrides.status;
  if (overrides?.assigneeId !== undefined) issue.assigneeId = overrides.assigneeId;
  return issue;
}
