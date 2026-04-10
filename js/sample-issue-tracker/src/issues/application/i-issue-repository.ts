import type { Issue } from "#/issues/domain";
import type { IssueId } from "#/issues/domain";
import { IssuePriority } from "#/issues/domain";
import { IssueStatus } from "#/issues/domain";
import type { PageRequest } from "#/shared-kernel";
import type { PageResult } from "#/shared-kernel";
import type { UserId } from "#/shared-kernel";

export interface IIssueRepository {
  getByIdAsync(id: IssueId): Promise<Issue | null>;
  listAsync(): Promise<Issue[]>;
  saveAsync(issue: Issue): Promise<void>;
  existsAsync(id: IssueId): Promise<boolean>;
  listBySprintAsync(sprintKey: string): Promise<Issue[]>;
  searchAsync(status: IssueStatus | null, priority: IssuePriority | null, assigneeId: UserId | null, sprintKey: string | null, page: PageRequest): Promise<PageResult<Issue>>;
}
