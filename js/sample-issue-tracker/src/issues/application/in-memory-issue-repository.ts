import { Enumerable } from "@meta-sharp/runtime";
import type { IIssueRepository } from "#/issues/application/i-issue-repository";
import type { Issue } from "#/issues/domain/issue";
import type { IssueId } from "#/issues/domain/issue-id";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueStatus } from "#/issues/domain/issue-status";
import type { PageRequest } from "#/shared-kernel/page-request";
import { PageResult } from "#/shared-kernel/page-result";
import type { UserId } from "#/shared-kernel/user-id";

export class InMemoryIssueRepository implements IIssueRepository {
  private readonly _issues: Issue[] = [];

  constructor() { }

  getByIdAsync(id: IssueId): Promise<Issue | null> {
    return Promise.resolve(Enumerable.from(this._issues).firstOrDefault((issue: Issue) => issue.id === id));
  }

  listAsync(): Promise<Issue[]> {
    return Promise.resolve(Enumerable.from(this._issues).orderBy((issue: Issue) => issue.createdAt).toArray());
  }

  saveAsync(entity: Issue): Promise<void> {
    const existingIndex = this._issues.findIndex((issue: Issue) => issue.id === entity.id);
    if (existingIndex >= 0) {
      this._issues[existingIndex] = entity;
    } else {
      this._issues.push(entity);
    }
    return Promise.resolve();
  }

  async existsAsync(id: IssueId): Promise<boolean> {
    return !(await this.getByIdAsync(id) === null);
  }

  listBySprintAsync(sprintKey: string): Promise<Issue[]> {
    return Promise.resolve(Enumerable.from(this._issues).where((issue: Issue) => issue.sprintKey === sprintKey).orderByDescending((issue: Issue) => issue.priority).thenBy((issue: Issue) => issue.title).toArray());
  }

  searchAsync(status: IssueStatus | null, priority: IssuePriority | null, assigneeId: UserId | null, sprintKey: string | null, page: PageRequest): Promise<PageResult<Issue>> {
    const filtered = Enumerable.from(this._issues).where((issue: Issue) => status === null || issue.status === status).where((issue: Issue) => priority === null || issue.priority === priority).where((issue: Issue) => assigneeId === null || issue.assigneeId === assigneeId).where((issue: Issue) => sprintKey === null || issue.sprintKey === sprintKey).orderByDescending((issue: Issue) => issue.priority).thenBy((issue: Issue) => issue.title).toArray();
    const items = Enumerable.from(filtered).skip(page.skip).take(page.safeSize).toArray();
    return Promise.resolve(new PageResult(items, filtered.length, page));
  }
}
