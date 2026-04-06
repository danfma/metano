import { Temporal } from "@js-temporal/polyfill";
import { isString } from "@meta-sharp/runtime";
import { Comment } from "./Comment";
import type { IssueId } from "./IssueId";
import { IssuePriority } from "./IssuePriority";
import { IssueStatus } from "./IssueStatus";
import { IssueType } from "./IssueType";
import { IssueWorkflow } from "./IssueWorkflow";
import { UserId } from "../../SharedKernel/UserId";
export class Issue {
  constructor(readonly id: IssueId, public title: string, public description: string, readonly type: IssueType, public priority: IssuePriority = "medium") { }

  status: IssueStatus = IssueStatus.Backlog;

  assigneeId: UserId | null = null;

  sprintKey: string | null = null;

  readonly createdAt: Temporal.ZonedDateTime = Temporal.Now.zonedDateTimeISO();

  updatedAt: Temporal.ZonedDateTime = Temporal.Now.zonedDateTimeISO();

  private readonly _comments: Comment[] = [];

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

  addComment(authorId: UserId, message: string, createdAt: Temporal.ZonedDateTime): void;
  addComment(authorId: UserId, message: string): void;
  addComment(...args: unknown[]): void {
    if (args.length === 3 && args[0] instanceof UserId && isString(args[1]) && typeof args[2] === "object") {
      const authorId = args[0] as UserId;
      const message = args[1] as string;
      const createdAt = args[2] as Temporal.ZonedDateTime;
      this._comments.push(new Comment(authorId, message, createdAt));
      this.touch(createdAt);
      return;
    }
    if (args.length === 2 && args[0] instanceof UserId && isString(args[1])) {
      const authorId = args[0] as UserId;
      const message = args[1] as string;
      this.addComment(authorId, message, Temporal.Now.zonedDateTimeISO());
      return;
    }
    throw new Error("No matching overload for addComment");
  }

  transitionTo(nextStatus: IssueStatus, actorId: UserId, changedAt: Temporal.ZonedDateTime): void;
  transitionTo(nextStatus: IssueStatus, actorId: UserId): void;
  transitionTo(...args: unknown[]): void {
    if (args.length === 3 && (args[0] === "backlog" || args[0] === "ready" || args[0] === "in-progress" || args[0] === "in-review" || args[0] === "done" || args[0] === "cancelled") && args[1] instanceof UserId && typeof args[2] === "object") {
      const nextStatus = args[0] as IssueStatus;
      const actorId = args[1] as UserId;
      const changedAt = args[2] as Temporal.ZonedDateTime;
      let previousStatus = this.status;
      if (!IssueWorkflow.canTransition(previousStatus, nextStatus)) {
        throw new Error(`Cannot transition issue from ${previousStatus} to ${nextStatus}.`);
      }
      this.status = nextStatus;
      this._comments.push(Comment.system(`Status changed from ${previousStatus} to ${nextStatus} by ${actorId}.`, changedAt));
      this.touch(changedAt);
      return;
    }
    if (args.length === 2 && (args[0] === "backlog" || args[0] === "ready" || args[0] === "in-progress" || args[0] === "in-review" || args[0] === "done" || args[0] === "cancelled") && args[1] instanceof UserId) {
      const nextStatus = args[0] as IssueStatus;
      const actorId = args[1] as UserId;
      this.transitionTo(nextStatus, actorId, Temporal.Now.zonedDateTimeISO());
      return;
    }
    throw new Error("No matching overload for transitionTo");
  }

  private touch(updatedAt: Temporal.ZonedDateTime): void {
    this.updatedAt = updatedAt;
  }
}
