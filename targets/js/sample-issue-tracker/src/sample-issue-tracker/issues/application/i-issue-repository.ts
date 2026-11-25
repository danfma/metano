/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { IReadOnlyList } from "metano-runtime/system/collections";
import { implementsInterface, type InterfaceType } from "metano-runtime";
import type { PageRequest } from "../../shared-kernel/page-request.js";
import type { PageResult } from "../../shared-kernel/page-result.js";
import type { UserId } from "../../shared-kernel/user-id.js";
import type { IssueId } from "../domain/issue-id.js";
import type { IssuePriority } from "../domain/issue-priority.js";
import type { IssueStatus } from "../domain/issue-status.js";
import type { Issue } from "../domain/issue.js";

export interface IIssueRepository {
  getByIdAsync(id: IssueId): Promise<Issue | null>;
  listAsync(): Promise<IReadOnlyList<Issue>>;
  saveAsync(issue: Issue): Promise<void>;
  existsAsync(id: IssueId): Promise<boolean>;
  listBySprintAsync(sprintKey: string): Promise<IReadOnlyList<Issue>>;
  searchAsync(
    status: IssueStatus | null,
    priority: IssuePriority | null,
    assigneeId: UserId | null,
    sprintKey: string | null,
    page: PageRequest,
  ): Promise<PageResult<Issue>>;
}

export const IIssueRepository$runtimeType: InterfaceType<IIssueRepository> = {
  kind: "interface",
  name: "IIssueRepository",
  fullName: "SampleIssueTracker.Issues.Application.IIssueRepository",
  namespace: "SampleIssueTracker.Issues.Application",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IIssueRepository => implementsInterface(v, IIssueRepository$runtimeType),
};
