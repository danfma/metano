/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Issue } from "#/sample-issue-tracker/issues/domain/issue";
import type { IssueId } from "#/sample-issue-tracker/issues/domain/issue-id";
import type { IssuePriority } from "#/sample-issue-tracker/issues/domain/issue-priority";
import type { IssueStatus } from "#/sample-issue-tracker/issues/domain/issue-status";
import type { PageRequest } from "#/sample-issue-tracker/shared-kernel/page-request";
import type { PageResult } from "#/sample-issue-tracker/shared-kernel/page-result";
import type { UserId } from "#/sample-issue-tracker/shared-kernel/user-id";

export interface IIssueRepository {
  getByIdAsync(id: IssueId): Promise<Issue | null>;
  listAsync(): Promise<Issue[]>;
  saveAsync(issue: Issue): Promise<void>;
  existsAsync(id: IssueId): Promise<boolean>;
  listBySprintAsync(sprintKey: string): Promise<Issue[]>;
  searchAsync(
    status: IssueStatus | null,
    priority: IssuePriority | null,
    assigneeId: UserId | null,
    sprintKey: string | null,
    page: PageRequest,
  ): Promise<PageResult<Issue>>;
}
