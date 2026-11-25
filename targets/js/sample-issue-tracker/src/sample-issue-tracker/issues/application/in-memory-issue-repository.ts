/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { List, type IReadOnlyList } from "metano-runtime/system/collections";
import type { ClassType } from "metano-runtime";
import {
  firstOrDefault,
  linq,
  orderBy,
  orderByDescending,
  skip,
  take,
  thenBy,
  toList,
  where,
} from "metano-runtime/system/linq";
import type { PageRequest } from "../../shared-kernel/page-request.js";
import { PageResult } from "../../shared-kernel/page-result.js";
import type { UserId } from "../../shared-kernel/user-id.js";
import type { IssueId } from "../domain/issue-id.js";
import type { IssuePriority } from "../domain/issue-priority.js";
import type { IssueStatus } from "../domain/issue-status.js";
import type { Issue } from "../domain/issue.js";
import { IIssueRepository$runtimeType, type IIssueRepository } from "./i-issue-repository.js";

let InMemoryIssueRepository$rt: ClassType<InMemoryIssueRepository> | undefined;

export class InMemoryIssueRepository implements IIssueRepository {
  private readonly _issues: List<Issue> = List.of<Issue>([]);

  constructor() {}

  static get $runtimeType(): ClassType<InMemoryIssueRepository> {
    return (InMemoryIssueRepository$rt ??= {
      kind: "class",
      name: "InMemoryIssueRepository",
      fullName: "SampleIssueTracker.Issues.Application.InMemoryIssueRepository",
      namespace: "SampleIssueTracker.Issues.Application",
      ctor: InMemoryIssueRepository,
      interfaces: [() => IIssueRepository$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new InMemoryIssueRepository(),
      test: (v: unknown): v is InMemoryIssueRepository => v instanceof InMemoryIssueRepository,
    });
  }

  getByIdAsync(id: IssueId): Promise<Issue | null> {
    return Promise.resolve(
      linq(
        this._issues,
        firstOrDefault((issue: Issue) => issue.id === id),
      ),
    );
  }

  listAsync(): Promise<IReadOnlyList<Issue>> {
    return Promise.resolve(
      linq(
        this._issues,
        orderBy((issue: Issue) => issue.createdAt),
        toList(),
      ),
    );
  }

  saveAsync(entity: Issue): Promise<void> {
    const existingIndex = this._issues.findIndex((issue: Issue) => issue.id === entity.id);

    if (existingIndex >= 0) {
      this._issues.set(existingIndex, entity);
    } else {
      this._issues.add(entity);
    }

    return Promise.resolve();
  }

  async existsAsync(id: IssueId): Promise<boolean> {
    return !((await this.getByIdAsync(id)) == null);
  }

  listBySprintAsync(sprintKey: string): Promise<IReadOnlyList<Issue>> {
    return Promise.resolve(
      linq(
        this._issues,
        where((issue: Issue) => issue.sprintKey === sprintKey),
        orderByDescending((issue: Issue) => issue.priority),
        thenBy((issue: Issue) => issue.title),
        toList(),
      ),
    );
  }

  searchAsync(
    status: IssueStatus | null,
    priority: IssuePriority | null,
    assigneeId: UserId | null,
    sprintKey: string | null,
    page: PageRequest,
  ): Promise<PageResult<Issue>> {
    const filtered = linq(
      this._issues,
      where(
        (issue: Issue) =>
          ((issue: Issue) =>
            ((issue: Issue) =>
              ((issue: Issue) => status == null || issue.status === status)(issue) &&
              ((issue: Issue) => priority == null || issue.priority === priority)(issue))(issue) &&
            ((issue: Issue) => assigneeId == null || issue.assigneeId === assigneeId)(issue))(
            issue,
          ) && ((issue: Issue) => sprintKey == null || issue.sprintKey === sprintKey)(issue),
      ),
      orderByDescending((issue: Issue) => issue.priority),
      thenBy((issue: Issue) => issue.title),
      toList(),
    );
    const items = linq(filtered, skip(page.skip), take(page.safeSize), toList());

    return Promise.resolve(new PageResult(items, filtered.count, page));
  }
}
