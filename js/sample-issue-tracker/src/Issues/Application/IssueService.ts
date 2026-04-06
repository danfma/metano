import { isString } from "@meta-sharp/runtime";
import type { IIssueRepository } from "./IIssueRepository";
import { Issue } from "../Domain/Issue";
import { IssueId } from "../Domain/IssueId";
import { IssuePriority } from "../Domain/IssuePriority";
import { IssueStatus } from "../Domain/IssueStatus";
import { IssueType } from "../Domain/IssueType";
import { OperationResult } from "../../SharedKernel/OperationResult";
import type { PageRequest } from "../../SharedKernel/PageRequest";
import type { PageResult } from "../../SharedKernel/PageResult";
import type { UserId } from "../../SharedKernel/UserId";
export class IssueService {
  constructor(repository: IIssueRepository) {
    this._repository = repository;
  }

  private readonly _repository: IIssueRepository;

  createAsync(title: string, description: string, type: IssueType, priority: IssuePriority): Promise<OperationResult<Issue>>;
  createAsync(title: string, description: string, type: IssueType): Promise<OperationResult<Issue>>;
  async createAsync(...args: unknown[]): Promise<unknown> {
    if (args.length === 4 && isString(args[0]) && isString(args[1]) && (args[2] === "story" || args[2] === "bug" || args[2] === "chore" || args[2] === "spike") && (args[3] === "low" || args[3] === "medium" || args[3] === "high" || args[3] === "urgent")) {
      const title = args[0] as string;
      const description = args[1] as string;
      const type = args[2] as IssueType;
      const priority = args[3] as IssuePriority;
      let issue = new Issue(IssueId.new(), title, description, type, priority);
      await this._repository.saveAsync(issue);
      return OperationResult.ok(issue);
    }
    if (args.length === 3 && isString(args[0]) && isString(args[1]) && (args[2] === "story" || args[2] === "bug" || args[2] === "chore" || args[2] === "spike")) {
      const title = args[0] as string;
      const description = args[1] as string;
      const type = args[2] as IssueType;
      return this.createAsync(title, description, type, IssuePriority.Medium);
    }
    throw new Error("No matching overload for createAsync");
  }

  async loadAsync(issueId: IssueId): Promise<OperationResult<Issue>> {
    let issue = await this._repository.getByIdAsync(issueId);
    return issue === null ? OperationResult.fail("issue_not_found", `Issue ${issueId} was not found.`) : OperationResult.ok(issue);
  }

  async assignAsync(issueId: IssueId, assigneeId: UserId): Promise<OperationResult<Issue>> {
    let loadResult = await this.loadAsync(issueId);
    if (!loadResult.hasValue || loadResult.value === null) {
      return loadResult;
    }
    loadResult.value.assignTo(assigneeId);
    await this._repository.saveAsync(loadResult.value);
    return loadResult;
  }

  async planSprintAsync(issueId: IssueId, sprintKey: string): Promise<OperationResult<Issue>> {
    let loadResult = await this.loadAsync(issueId);
    if (!loadResult.hasValue || loadResult.value === null) {
      return loadResult;
    }
    loadResult.value.planForSprint(sprintKey);
    await this._repository.saveAsync(loadResult.value);
    return loadResult;
  }

  addCommentAsync(issueId: IssueId, authorId: UserId, message: string): Promise<OperationResult<Issue>>;
  addCommentAsync(issue: Issue, authorId: UserId, message: string): Promise<OperationResult<Issue>>;
  async addCommentAsync(...args: unknown[]): Promise<unknown> {
    if (args.length === 3 && typeof args[0] === "string" && typeof args[1] === "string" && isString(args[2])) {
      const issueId = args[0] as IssueId;
      const authorId = args[1] as UserId;
      const message = args[2] as string;
      let loadResult = await this.loadAsync(issueId);
      if (!loadResult.hasValue || loadResult.value === null) {
        return loadResult;
      }
      return await this.addCommentAsync(loadResult.value, authorId, message);
    }
    if (args.length === 3 && args[0] instanceof Issue && typeof args[1] === "string" && isString(args[2])) {
      const issue = args[0] as Issue;
      const authorId = args[1] as UserId;
      const message = args[2] as string;
      issue.addComment(authorId, message);
      await this._repository.saveAsync(issue);
      return OperationResult.ok(issue);
    }
    throw new Error("No matching overload for addCommentAsync");
  }

  async transitionAsync(issueId: IssueId, nextStatus: IssueStatus, actorId: UserId): Promise<OperationResult<Issue>> {
    let loadResult = await this.loadAsync(issueId);
    if (!loadResult.hasValue || loadResult.value === null) {
      return loadResult;
    }
    loadResult.value.transitionTo(nextStatus, actorId);
    await this._repository.saveAsync(loadResult.value);
    return loadResult;
  }

  searchAsync(status: IssueStatus | null, priority: IssuePriority | null, assigneeId: UserId | null, sprintKey: string | null, page: PageRequest): Promise<PageResult<Issue>> {
    return this._repository.searchAsync(status, priority, assigneeId, sprintKey, page);
  }
}
