/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { Temporal } from "@js-temporal/polyfill";
import { List, type IReadOnlyList } from "metano-runtime/system/collections";
import { isString, type ClassType } from "metano-runtime";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import type { UserId } from "../../shared-kernel/user-id.js";
import { Comment } from "./comment.js";
import type { IssueId } from "./issue-id.js";
import type { IssuePriority } from "./issue-priority.js";
import { IssueStatus } from "./issue-status.js";
import type { IssueType } from "./issue-type.js";
import { IssueWorkflow } from "./issue-workflow.js";

let Issue$rt: ClassType<Issue> | undefined;

export class Issue {
  declare readonly id: IssueId;
  declare title: string;
  declare description: string;
  declare readonly type: IssueType;
  declare priority: IssuePriority;
  declare status: IssueStatus;
  declare assigneeId: UserId | null;
  declare sprintKey: string | null;
  declare readonly createdAt: Temporal.ZonedDateTime;
  declare updatedAt: Temporal.ZonedDateTime;
  private declare readonly _comments: List<Comment>;

  constructor(
    id: IssueId,
    title: string,
    description: string,
    type: IssueType,
    priority: IssuePriority = "medium",
  ) {
    this.id = id;
    this.title = title;
    this.description = description;
    this.type = type;
    this.priority = priority;
    this.status = IssueStatus.Backlog;
    this.assigneeId = null;
    this.sprintKey = null;
    this.createdAt = Temporal.Now.zonedDateTimeISO();
    this.updatedAt = Temporal.Now.zonedDateTimeISO();
    this._comments = List.of<Comment>([]);
  }

  static get $runtimeType(): ClassType<Issue> {
    return (Issue$rt ??= {
      kind: "class",
      name: "Issue",
      fullName: "SampleIssueTracker.Issues.Domain.Issue",
      namespace: "SampleIssueTracker.Issues.Domain",
      ctor: Issue,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Issue => v instanceof Issue,
    });
  }

  get comments(): IReadOnlyList<Comment> {
    return this._comments;
  }

  get isClosed(): boolean {
    return this.status === IssueStatus.Done || this.status === IssueStatus.Cancelled;
  }

  get commentCount(): number {
    return this._comments.count;
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

  addCommentAuthorIdMessageCreatedAt(
    authorId: UserId,
    message: string,
    createdAt: Temporal.ZonedDateTime,
  ): void {
    this._comments.add(new Comment(authorId, message, createdAt));
    this.touch(createdAt);
  }

  addCommentAuthorIdMessage(authorId: UserId, message: string): void {
    this.addCommentAuthorIdMessageCreatedAt(authorId, message, Temporal.Now.zonedDateTimeISO());
  }

  addComment(authorId: UserId, message: string, createdAt: Temporal.ZonedDateTime): void;
  addComment(authorId: UserId, message: string): void;
  addComment(...args: unknown[]): void {
    if (
      args.length === 3 &&
      isString(args[0]) &&
      isString(args[1]) &&
      Object.prototype.toString.call(args[2]) === "[object Temporal.ZonedDateTime]"
    ) {
      this.addCommentAuthorIdMessageCreatedAt(
        args[0] as UserId,
        args[1] as string,
        args[2] as Temporal.ZonedDateTime,
      );

      return;
    }

    if (args.length === 2 && isString(args[0]) && isString(args[1])) {
      this.addCommentAuthorIdMessage(args[0] as UserId, args[1] as string);

      return;
    }

    throw new Error("No matching overload for addComment");
  }

  transitionToNextStatusActorIdChangedAt(
    nextStatus: IssueStatus,
    actorId: UserId,
    changedAt: Temporal.ZonedDateTime,
  ): void {
    const previousStatus = this.status;

    if (!IssueWorkflow.canTransition(previousStatus, nextStatus)) {
      throw new InvalidOperationException(
        `Cannot transition issue from ${previousStatus} to ${nextStatus}.`,
      );
    }

    this.status = nextStatus;
    this._comments.add(
      Comment.system(
        `Status changed from ${previousStatus} to ${nextStatus} by ${actorId}.`,
        changedAt,
      ),
    );
    this.touch(changedAt);
  }

  transitionToNextStatusActorId(nextStatus: IssueStatus, actorId: UserId): void {
    this.transitionToNextStatusActorIdChangedAt(
      nextStatus,
      actorId,
      Temporal.Now.zonedDateTimeISO(),
    );
  }

  transitionTo(nextStatus: IssueStatus, actorId: UserId, changedAt: Temporal.ZonedDateTime): void;
  transitionTo(nextStatus: IssueStatus, actorId: UserId): void;
  transitionTo(...args: unknown[]): void {
    if (
      args.length === 3 &&
      (args[0] === "backlog" ||
        args[0] === "ready" ||
        args[0] === "in-progress" ||
        args[0] === "in-review" ||
        args[0] === "done" ||
        args[0] === "cancelled") &&
      isString(args[1]) &&
      Object.prototype.toString.call(args[2]) === "[object Temporal.ZonedDateTime]"
    ) {
      this.transitionToNextStatusActorIdChangedAt(
        args[0] as IssueStatus,
        args[1] as UserId,
        args[2] as Temporal.ZonedDateTime,
      );

      return;
    }

    if (
      args.length === 2 &&
      (args[0] === "backlog" ||
        args[0] === "ready" ||
        args[0] === "in-progress" ||
        args[0] === "in-review" ||
        args[0] === "done" ||
        args[0] === "cancelled") &&
      isString(args[1])
    ) {
      this.transitionToNextStatusActorId(args[0] as IssueStatus, args[1] as UserId);

      return;
    }

    throw new Error("No matching overload for transitionTo");
  }

  private touch(updatedAt: Temporal.ZonedDateTime): void {
    this.updatedAt = updatedAt;
  }
}
