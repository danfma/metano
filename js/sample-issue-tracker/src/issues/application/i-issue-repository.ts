import type { Issue } from "../domain/issue";
import type { IssueId } from "../domain/issue-id";
import { IssuePriority } from "../domain/issue-priority";
import { IssueStatus } from "../domain/issue-status";
import type { PageRequest } from "../../shared-kernel/page-request";
import type { PageResult } from "../../shared-kernel/page-result";
import type { UserId } from "../../shared-kernel/user-id";
export interface IIssueRepository {
  getByIdAsync(id: IssueId): Promise<Issue | null>;
  listAsync(): Promise<Issue[]>;
  saveAsync(issue: Issue): Promise<void>;
  existsAsync(id: IssueId): Promise<boolean>;
  listBySprintAsync(sprintKey: string): Promise<Issue[]>;
  searchAsync(status: IssueStatus | null, priority: IssuePriority | null, assigneeId: UserId | null, sprintKey: string | null, page: PageRequest): Promise<PageResult<Issue>>;
}
