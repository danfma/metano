/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Issue, IssueId, IssuePriority, IssueStatus } from "#/issues/domain";
import type { PageRequest, PageResult, UserId } from "#/shared-kernel";

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
