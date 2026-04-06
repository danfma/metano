import { Enumerable } from "@meta-sharp/runtime";
import type { IIssueRepository } from "./IIssueRepository";
import type { Issue } from "../Domain/Issue";
import type { IssueId } from "../Domain/IssueId";
import { IssuePriority } from "../Domain/IssuePriority";
import { IssueStatus } from "../Domain/IssueStatus";
import type { PageRequest } from "../../SharedKernel/PageRequest";
import { PageResult } from "../../SharedKernel/PageResult";
import type { UserId } from "../../SharedKernel/UserId";
export class InMemoryIssueRepository implements IIssueRepository {
  constructor() { }

  private readonly _issues: Issue[] = [];

  getByIdAsync(id: IssueId): Promise<Issue | null> {
    return Promise.resolve(Enumerable.from(this._issues).firstOrDefault((issue: Issue) => issue.id === id));
  }

  listAsync(): Promise<Issue[]> {
    return Promise.resolve(Enumerable.from(this._issues).orderBy((issue: Issue) => issue.createdAt).toArray());
  }

  saveAsync(entity: Issue): Promise<void> {
    let existingIndex = this._issues.findIndex((issue: Issue) => issue.id === entity.id);
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
    let filtered = Enumerable.from(this._issues).where((issue: Issue) => status === null || issue.status === status).where((issue: Issue) => priority === null || issue.priority === priority).where((issue: Issue) => assigneeId === null || issue.assigneeId === assigneeId).where((issue: Issue) => sprintKey === null || issue.sprintKey === sprintKey).orderByDescending((issue: Issue) => issue.priority).thenBy((issue: Issue) => issue.title).toArray();
    let items = Enumerable.from(filtered).skip(page.skip).take(page.safeSize).toArray();
    return Promise.resolve(new PageResult(items, filtered.length, page));
  }
}
