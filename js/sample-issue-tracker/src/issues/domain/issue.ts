import { Temporal } from "@js-temporal/polyfill";
import { isString } from "metano-runtime";
import { Comment } from "#/issues/domain/comment";
import type { IssueId } from "#/issues/domain/issue-id";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssueType } from "#/issues/domain/issue-type";
import { IssueWorkflow } from "#/issues/domain/issue-workflow";
import type { UserId } from "#/shared-kernel";

export class Issue {
  status: IssueStatus = IssueStatus.Backlog;

  assigneeId: UserId | null = null;

  sprintKey: string | null = null;

  readonly createdAt: Temporal.ZonedDateTime = Temporal.Now.zonedDateTimeISO();

  updatedAt: Temporal.ZonedDateTime = Temporal.Now.zonedDateTimeISO();

  private readonly _comments: Comment[] = [];

  constructor(readonly id: IssueId, public title: string, public description: string, readonly type: IssueType, public priority: IssuePriority = "medium") { }

  get comments(): Comment[] {
    return this._comments;
  }

  get isClosed(): boolean {
    return this.status === IssueStatus.Done || this.status === IssueStatus.Cancelled;
  }

  get commentCount(): number {
    return this._comments.length;
  }

  get lane(): string {
    return IssueWorkflow.describeLane(this);
  }

  rename(newTitle: string): void {
    this.title = newTitle;
    this.touch(Temporal.Now.zonedDateTimeISO());
  }

  describe(newDescription: string): void {
    this.description = newDescription;
    this.touch(Temporal.Now.zonedDateTimeISO());
  }

  changePriority(newPriority: IssuePriority): void {
    this.priority = newPriority;
    this.touch(Temporal.Now.zonedDateTimeISO());
  }

  assignTo(assigneeId: UserId): void {
    this.assigneeId = assigneeId;
    this.touch(Temporal.Now.zonedDateTimeISO());
  }

  unassign(): void {
    this.assigneeId = null;
    this.touch(Temporal.Now.zonedDateTimeISO());
  }

  planForSprint(sprintKey: string): void {
    this.sprintKey = sprintKey;
    this.touch(Temporal.Now.zonedDateTimeISO());
  }

  removeFromSprint(): void {
    this.sprintKey = null;
    this.touch(Temporal.Now.zonedDateTimeISO());
  }

  private addCommentAuthorIdMessageCreatedAt(authorId: UserId, message: string, createdAt: Temporal.ZonedDateTime): void {
    this._comments.push(new Comment(authorId, message, createdAt));
    this.touch(createdAt);
  }

  private addCommentAuthorIdMessage(authorId: UserId, message: string): void {
    this.addComment(authorId, message, Temporal.Now.zonedDateTimeISO());
  }

  addComment(authorId: UserId, message: string, createdAt: Temporal.ZonedDateTime): void;
  addComment(authorId: UserId, message: string): void;
  addComment(...args: unknown[]): void {
    if (args.length === 3 && typeof args[0] === "string" && isString(args[1]) && typeof args[2] === "object") {
      this.addCommentAuthorIdMessageCreatedAt(args[0] as UserId, args[1] as string, args[2] as Temporal.ZonedDateTime);
      return;
    }
    if (args.length === 2 && typeof args[0] === "string" && isString(args[1])) {
      this.addCommentAuthorIdMessage(args[0] as UserId, args[1] as string);
      return;
    }
    throw new Error("No matching overload for addComment");
  }

  private transitionToNextStatusActorIdChangedAt(nextStatus: IssueStatus, actorId: UserId, changedAt: Temporal.ZonedDateTime): void {
    const previousStatus = this.status;
    if (!IssueWorkflow.canTransition(previousStatus, nextStatus)) {
      throw new Error(`Cannot transition issue from ${previousStatus} to ${nextStatus}.`);
    }
    this.status = nextStatus;
    this._comments.push(Comment.system(`Status changed from ${previousStatus} to ${nextStatus} by ${actorId}.`, changedAt));
    this.touch(changedAt);
  }

  private transitionToNextStatusActorId(nextStatus: IssueStatus, actorId: UserId): void {
    this.transitionTo(nextStatus, actorId, Temporal.Now.zonedDateTimeISO());
  }

  transitionTo(nextStatus: IssueStatus, actorId: UserId, changedAt: Temporal.ZonedDateTime): void;
  transitionTo(nextStatus: IssueStatus, actorId: UserId): void;
  transitionTo(...args: unknown[]): void {
    if (args.length === 3 && (args[0] === "backlog" || args[0] === "ready" || args[0] === "in-progress" || args[0] === "in-review" || args[0] === "done" || args[0] === "cancelled") && typeof args[1] === "string" && typeof args[2] === "object") {
      this.transitionToNextStatusActorIdChangedAt(args[0] as IssueStatus, args[1] as UserId, args[2] as Temporal.ZonedDateTime);
      return;
    }
    if (args.length === 2 && (args[0] === "backlog" || args[0] === "ready" || args[0] === "in-progress" || args[0] === "in-review" || args[0] === "done" || args[0] === "cancelled") && typeof args[1] === "string") {
      this.transitionToNextStatusActorId(args[0] as IssueStatus, args[1] as UserId);
      return;
    }
    throw new Error("No matching overload for transitionTo");
  }

  private touch(updatedAt: Temporal.ZonedDateTime): void {
    this.updatedAt = updatedAt;
  }
}
