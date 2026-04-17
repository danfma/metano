import { isString } from "metano-runtime";
import { Issue, IssueId, IssuePriority, IssueStatus, IssueType } from "#/issues/domain";
import { OperationResult, type PageRequest, type PageResult, type UserId } from "#/shared-kernel";
import type { IIssueRepository } from "./i-issue-repository";

export class IssueService {
  private readonly _repository: IIssueRepository;

  constructor(repository: IIssueRepository) {
    this._repository = repository;
  }

  private async createAsyncTitleDescriptionTypePriority(title: string, description: string, type: IssueType, priority: IssuePriority): Promise<OperationResult<Issue>> {
    const issue = new Issue(IssueId.new_(), title, description, type, priority);
    await this._repository.saveAsync(issue);

    return OperationResult.ok(issue);
  }

  private createAsyncTitleDescriptionType(title: string, description: string, type: IssueType): Promise<OperationResult<Issue>> {
    return this.createAsync(title, description, type, IssuePriority.Medium);
  }

  createAsync(title: string, description: string, type: IssueType, priority: IssuePriority): Promise<OperationResult<Issue>>;
  createAsync(title: string, description: string, type: IssueType): Promise<OperationResult<Issue>>;
  async createAsync(...args: unknown[]): Promise<unknown> {
    if (args.length === 4 && isString(args[0]) && isString(args[1]) && (args[2] === "story" || args[2] === "bug" || args[2] === "chore" || args[2] === "spike") && (args[3] === "low" || args[3] === "medium" || args[3] === "high" || args[3] === "urgent")) {
      return this.createAsyncTitleDescriptionTypePriority(args[0] as string, args[1] as string, args[2] as IssueType, args[3] as IssuePriority);
    }

    if (args.length === 3 && isString(args[0]) && isString(args[1]) && (args[2] === "story" || args[2] === "bug" || args[2] === "chore" || args[2] === "spike")) {
      return this.createAsyncTitleDescriptionType(args[0] as string, args[1] as string, args[2] as IssueType);
    }

    throw new Error("No matching overload for createAsync");
  }

  async loadAsync(issueId: IssueId): Promise<OperationResult<Issue>> {
    const issue = await this._repository.getByIdAsync(issueId);

    return issue === null ? OperationResult.fail("issue_not_found", `Issue ${issueId} was not found.`) : OperationResult.ok(issue);
  }

  async assignAsync(issueId: IssueId, assigneeId: UserId): Promise<OperationResult<Issue>> {
    const loadResult = await this.loadAsync(issueId);

    if (!loadResult.hasValue || loadResult.value === null) {
      return loadResult;
    }

    loadResult.value.assignTo(assigneeId);
    await this._repository.saveAsync(loadResult.value);

    return loadResult;
  }

  async planSprintAsync(issueId: IssueId, sprintKey: string): Promise<OperationResult<Issue>> {
    const loadResult = await this.loadAsync(issueId);

    if (!loadResult.hasValue || loadResult.value === null) {
      return loadResult;
    }

    loadResult.value.planForSprint(sprintKey);
    await this._repository.saveAsync(loadResult.value);

    return loadResult;
  }

  private async addCommentAsyncIssueIdAuthorIdMessage(issueId: IssueId, authorId: UserId, message: string): Promise<OperationResult<Issue>> {
    const loadResult = await this.loadAsync(issueId);

    if (!loadResult.hasValue || loadResult.value === null) {
      return loadResult;
    }

    return await this.addCommentAsync(loadResult.value, authorId, message);
  }

  private async addCommentAsyncIssueAuthorIdMessage(issue: Issue, authorId: UserId, message: string): Promise<OperationResult<Issue>> {
    issue.addComment(authorId, message);
    await this._repository.saveAsync(issue);

    return OperationResult.ok(issue);
  }

  addCommentAsync(issueId: IssueId, authorId: UserId, message: string): Promise<OperationResult<Issue>>;
  addCommentAsync(issue: Issue, authorId: UserId, message: string): Promise<OperationResult<Issue>>;
  async addCommentAsync(...args: unknown[]): Promise<unknown> {
    if (args.length === 3 && typeof args[0] === "string" && typeof args[1] === "string" && isString(args[2])) {
      return this.addCommentAsyncIssueIdAuthorIdMessage(args[0] as IssueId, args[1] as UserId, args[2] as string);
    }

    if (args.length === 3 && args[0] instanceof Issue && typeof args[1] === "string" && isString(args[2])) {
      return this.addCommentAsyncIssueAuthorIdMessage(args[0] as Issue, args[1] as UserId, args[2] as string);
    }

    throw new Error("No matching overload for addCommentAsync");
  }

  async transitionAsync(issueId: IssueId, nextStatus: IssueStatus, actorId: UserId): Promise<OperationResult<Issue>> {
    const loadResult = await this.loadAsync(issueId);

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
