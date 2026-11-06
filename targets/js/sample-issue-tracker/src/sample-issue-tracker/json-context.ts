/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { Temporal } from "@js-temporal/polyfill";
import { Decimal } from "decimal.js";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { ActivityFeed } from "#/sample-issue-tracker/issues/domain/activity-feed";
import { Comment } from "#/sample-issue-tracker/issues/domain/comment";
import { IssueActivity } from "#/sample-issue-tracker/issues/domain/issue-activity";
import { IssueActivityKind } from "#/sample-issue-tracker/issues/domain/issue-activity-kind";
import { IssueId } from "#/sample-issue-tracker/issues/domain/issue-id";
import { IssuePriority } from "#/sample-issue-tracker/issues/domain/issue-priority";
import { IssueSnapshot } from "#/sample-issue-tracker/issues/domain/issue-snapshot";
import { IssueStatus } from "#/sample-issue-tracker/issues/domain/issue-status";
import { IssueType } from "#/sample-issue-tracker/issues/domain/issue-type";
import { SnowflakeId } from "#/sample-issue-tracker/shared-kernel/snowflake-id";
import { UserId } from "#/sample-issue-tracker/shared-kernel/user-id";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _issueSnapshot?: TypeSpec<IssueSnapshot>;

  private _comment?: TypeSpec<Comment>;

  private _iReadOnlyListComment?: ValueSpec<Comment[] | null>;

  private _issueActivity?: TypeSpec<IssueActivity>;

  private _issueActivityArray?: ValueSpec<IssueActivity[] | null>;

  private _activityFeed?: TypeSpec<ActivityFeed>;

  static get default(): JsonContext {
    return JsonContext._default;
  }

  get issueSnapshot(): TypeSpec<IssueSnapshot> {
    return (this._issueSnapshot ??= this.createSpec({
      type: IssueSnapshot,
      factory: (p: Record<string, unknown>) =>
        new IssueSnapshot(
          p.id as IssueId,
          p.title as string,
          p.description as string,
          p.type as IssueType,
          p.priority as IssuePriority,
          p.status as IssueStatus,
          p.assigneeId as UserId | null,
          p.sprintKey as string | null,
          p.createdAt as Temporal.ZonedDateTime,
          p.updatedAt as Temporal.ZonedDateTime,
          p.estimatedHours as Decimal,
          p.comments as Comment[],
        ),
      properties: [
        {
          ts: "id",
          json: "id",
          type: {
            kind: "branded",
            create: IssueId.create,
            typeName: "SampleIssueTracker.Issues.Domain.IssueId",
          },
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
          type: {
            kind: "enum",
            values: IssueType,
            typeName: "SampleIssueTracker.Issues.Domain.IssueType",
          },
        },
        {
          ts: "priority",
          json: "priority",
          type: {
            kind: "enum",
            values: IssuePriority,
            typeName: "SampleIssueTracker.Issues.Domain.IssuePriority",
          },
        },
        {
          ts: "status",
          json: "status",
          type: {
            kind: "enum",
            values: IssueStatus,
            typeName: "SampleIssueTracker.Issues.Domain.IssueStatus",
          },
        },
        {
          ts: "assigneeId",
          json: "assigneeId",
          type: {
            kind: "nullable",
            inner: {
              kind: "branded",
              create: UserId.from,
              typeName: "SampleIssueTracker.SharedKernel.UserId",
            },
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
          type: {
            kind: "temporal",
            parse: Temporal.ZonedDateTime.from,
            typeName: "System.DateTimeOffset",
          },
        },
        {
          ts: "updatedAt",
          json: "updatedAt",
          type: {
            kind: "temporal",
            parse: Temporal.ZonedDateTime.from,
            typeName: "System.DateTimeOffset",
          },
        },
        {
          ts: "estimatedHours",
          json: "estimatedHours",
          type: { kind: "decimal", type: Decimal, typeName: "System.Decimal" },
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
    }));
  }

  get comment(): TypeSpec<Comment> {
    return (this._comment ??= this.createSpec({
      type: Comment,
      factory: (p: Record<string, unknown>) =>
        new Comment(
          p.authorId as UserId,
          p.message as string,
          p.createdAt as Temporal.ZonedDateTime,
          p.isSystem as boolean,
        ),
      properties: [
        {
          ts: "authorId",
          json: "authorId",
          type: {
            kind: "branded",
            create: UserId.from,
            typeName: "SampleIssueTracker.SharedKernel.UserId",
          },
        },
        {
          ts: "message",
          json: "message",
          type: { kind: "primitive" },
        },
        {
          ts: "createdAt",
          json: "createdAt",
          type: {
            kind: "temporal",
            parse: Temporal.ZonedDateTime.from,
            typeName: "System.DateTimeOffset",
          },
        },
        {
          ts: "isSystem",
          json: "isSystem",
          type: { kind: "primitive", typeName: "System.Boolean" },
        },
      ],
    }));
  }

  get iReadOnlyListComment(): ValueSpec<Comment[] | null> {
    return (this._iReadOnlyListComment ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.comment,
        },
      },
    }));
  }

  get issueActivity(): TypeSpec<IssueActivity> {
    return (this._issueActivity ??= this.createSpec({
      type: IssueActivity,
      factory: (p: Record<string, unknown>) =>
        new IssueActivity(
          p.id as SnowflakeId,
          p.issueKey as string,
          p.kind as IssueActivityKind,
          p.occurredAtTicks as bigint,
          p.durationTicks as bigint | null,
        ),
      properties: [
        {
          ts: "id",
          json: "id",
          type: {
            kind: "int64",
            numberType: "System.Int64",
            typeName: "SampleIssueTracker.SharedKernel.SnowflakeId",
            create: SnowflakeId.from,
          },
        },
        {
          ts: "issueKey",
          json: "issueKey",
          type: { kind: "primitive" },
        },
        {
          ts: "kind",
          json: "kind",
          type: {
            kind: "enum",
            values: IssueActivityKind,
            typeName: "SampleIssueTracker.Issues.Domain.IssueActivityKind",
          },
        },
        {
          ts: "occurredAtTicks",
          json: "occurredAtTicks",
          type: { kind: "int64", numberType: "System.Int64" },
        },
        {
          ts: "durationTicks",
          json: "durationTicks",
          type: {
            kind: "nullable",
            inner: { kind: "int64", numberType: "System.Int64" },
          },
          optional: true,
        },
      ],
    }));
  }

  get issueActivityArray(): ValueSpec<IssueActivity[] | null> {
    return (this._issueActivityArray ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.issueActivity,
        },
      },
    }));
  }

  get activityFeed(): TypeSpec<ActivityFeed> {
    return (this._activityFeed ??= this.createSpec({
      type: ActivityFeed,
      factory: (p: Record<string, unknown>) =>
        new ActivityFeed(p.entries as Map<bigint, IssueActivity>),
      properties: [
        {
          ts: "entries",
          json: "entries",
          type: {
            kind: "map",
            key: { kind: "int64", numberType: "System.Int64" },
            value: {
              kind: "ref",
              spec: () => this.issueActivity,
            },
          },
        },
      ],
    }));
  }
}
