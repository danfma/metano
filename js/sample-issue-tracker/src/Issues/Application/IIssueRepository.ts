import type { Issue } from "../Domain/Issue";
import type { IssueId } from "../Domain/IssueId";
import { IssuePriority } from "../Domain/IssuePriority";
import { IssueStatus } from "../Domain/IssueStatus";
import type { PageRequest } from "../../SharedKernel/PageRequest";
import type { PageResult } from "../../SharedKernel/PageResult";
import type { UserId } from "../../SharedKernel/UserId";
export interface IIssueRepository {
  getByIdAsync(id: IssueId): Promise<Issue | null>;
  listAsync(): Promise<Issue[]>;
  saveAsync(issue: Issue): Promise<void>;
  existsAsync(id: IssueId): Promise<boolean>;
  listBySprintAsync(sprintKey: string): Promise<Issue[]>;
  searchAsync(status: IssueStatus | null, priority: IssuePriority | null, assigneeId: UserId | null, sprintKey: string | null, page: PageRequest): Promise<PageResult<Issue>>;
}
