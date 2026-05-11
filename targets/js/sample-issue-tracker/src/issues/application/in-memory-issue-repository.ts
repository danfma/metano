/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { firstOrDefault, linq, orderBy, orderByDescending, skip, take, thenBy, toArray, where } from "metano-runtime/system/linq";
import type { Issue, IssueId, IssuePriority, IssueStatus } from "#/issues/domain";
import { PageResult, type PageRequest, type UserId } from "#/shared-kernel";
import type { IIssueRepository } from "./i-issue-repository";

export class InMemoryIssueRepository implements IIssueRepository {
  private readonly _issues: Issue[] = [];

  constructor() { }

  getByIdAsync(id: IssueId): Promise<Issue | null> {
    return Promise.resolve(linq(this._issues, firstOrDefault((issue: Issue) => issue.id === id)));
  }

  listAsync(): Promise<Issue[]> {
    return Promise.resolve(linq(this._issues, orderBy((issue: Issue) => issue.createdAt), toArray()));
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
    return !(await this.getByIdAsync(id) == null);
  }

  listBySprintAsync(sprintKey: string): Promise<Issue[]> {
    return Promise.resolve(linq(this._issues, where((issue: Issue) => issue.sprintKey === sprintKey), orderByDescending((issue: Issue) => issue.priority), thenBy((issue: Issue) => issue.title), toArray()));
  }

  searchAsync(status: IssueStatus | null, priority: IssuePriority | null, assigneeId: UserId | null, sprintKey: string | null, page: PageRequest): Promise<PageResult<Issue>> {
    const filtered = linq(this._issues, where((issue: Issue) => status == null || issue.status === status), where((issue: Issue) => priority == null || issue.priority === priority), where((issue: Issue) => assigneeId == null || issue.assigneeId === assigneeId), where((issue: Issue) => sprintKey == null || issue.sprintKey === sprintKey), orderByDescending((issue: Issue) => issue.priority), thenBy((issue: Issue) => issue.title), toArray());
    const items = linq(filtered, skip(page.skip), take(page.safeSize), toArray());

    return Promise.resolve(new PageResult(items, filtered.length, page));
  }
}
