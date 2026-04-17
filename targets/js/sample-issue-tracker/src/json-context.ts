import { Temporal } from "@js-temporal/polyfill";
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Decimal } from "decimal.js";
import { Comment, IssueId, IssuePriority, IssueSnapshot, IssueStatus, IssueType } from "#/issues/domain";
import { UserId } from "#/shared-kernel";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _issueSnapshot?: TypeSpec<IssueSnapshot>;

  private _comment?: TypeSpec<Comment>;

  static get default(): JsonContext {
    return this._default;
  }

  get issueSnapshot(): TypeSpec<IssueSnapshot> {
    return this._issueSnapshot ??= this.createSpec({
      type: IssueSnapshot,
      factory: (p: Record<string, unknown>) => new IssueSnapshot(p.id as IssueId, p.title as string, p.description as string, p.type as IssueType, p.priority as IssuePriority, p.status as IssueStatus, p.assigneeId as UserId | null, p.sprintKey as string | null, p.createdAt as Temporal.ZonedDateTime, p.updatedAt as Temporal.ZonedDateTime, p.estimatedHours as Decimal, p.comments as Comment[]),
      properties: [
        {
          ts: "id",
          json: "id",
          type: { kind: "branded", create: IssueId.create },
        },
        {
          ts: "title",
          json: "title",
          type: { kind: "primitive" },
        },
        {
          ts: "description",
          json: "description",
          type: { kind: "primitive" },
        },
        {
          ts: "type",
          json: "type",
          type: { kind: "enum", values: IssueType },
        },
        {
          ts: "priority",
          json: "priority",
          type: { kind: "enum", values: IssuePriority },
        },
        {
          ts: "status",
          json: "status",
          type: { kind: "enum", values: IssueStatus },
        },
        {
          ts: "assigneeId",
          json: "assigneeId",
          type: {
            kind: "nullable",
            inner: { kind: "branded", create: UserId.create },
          },
          optional: true,
        },
        {
          ts: "sprintKey",
          json: "sprintKey",
          type: {
            kind: "nullable",
            inner: { kind: "primitive" },
          },
          optional: true,
        },
        {
          ts: "createdAt",
          json: "createdAt",
          type: { kind: "temporal", parse: Temporal.ZonedDateTime.from },
        },
        {
          ts: "updatedAt",
          json: "updatedAt",
          type: { kind: "temporal", parse: Temporal.ZonedDateTime.from },
        },
        {
          ts: "estimatedHours",
          json: "estimatedHours",
          type: { kind: "decimal" },
        },
        {
          ts: "comments",
          json: "comments",
          type: {
            kind: "array",
            element: {
              kind: "ref",
              spec: () => this.comment,
            },
          },
        },
      ],
    });
  }

  get comment(): TypeSpec<Comment> {
    return this._comment ??= this.createSpec({
      type: Comment,
      factory: (p: Record<string, unknown>) => new Comment(p.authorId as UserId, p.message as string, p.createdAt as Temporal.ZonedDateTime, p.isSystem as boolean),
      properties: [
        {
          ts: "authorId",
          json: "authorId",
          type: { kind: "branded", create: UserId.create },
        },
        {
          ts: "message",
          json: "message",
          type: { kind: "primitive" },
        },
        {
          ts: "createdAt",
          json: "createdAt",
          type: { kind: "temporal", parse: Temporal.ZonedDateTime.from },
        },
        {
          ts: "isSystem",
          json: "isSystem",
          type: { kind: "primitive" },
        },
      ],
    });
  }
}
