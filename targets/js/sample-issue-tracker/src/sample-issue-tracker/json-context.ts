/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { Temporal } from "@js-temporal/polyfill";
import {
  Dictionary,
  List,
  type IReadOnlyDictionary,
  type IReadOnlyList,
} from "metano-runtime/system/collections";
import type { ClassType } from "metano-runtime";
import { Decimal } from "metano-runtime/system/decimal";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { ActivityFeed } from "./issues/domain/activity-feed.js";
import { Comment } from "./issues/domain/comment.js";
import { IssueActivityKind } from "./issues/domain/issue-activity-kind.js";
import { IssueActivity } from "./issues/domain/issue-activity.js";
import { IssueId } from "./issues/domain/issue-id.js";
import { IssuePriority } from "./issues/domain/issue-priority.js";
import { IssueSnapshot } from "./issues/domain/issue-snapshot.js";
import { IssueStatus } from "./issues/domain/issue-status.js";
import { IssueType } from "./issues/domain/issue-type.js";
import { SnowflakeId } from "./shared-kernel/snowflake-id.js";
import { UserId } from "./shared-kernel/user-id.js";

let JsonContext$rt: ClassType<JsonContext> | undefined;

export class JsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new JsonContext() as JsonContext;
  private _issueSnapshot?: TypeSpec<IssueSnapshot>;
  private _comment?: TypeSpec<Comment>;
  private _iReadOnlyListComment?: ValueSpec<IReadOnlyList<Comment> | null>;
  private _issueActivity?: TypeSpec<IssueActivity>;
  private _issueActivityArray?: ValueSpec<IssueActivity[] | null>;
  private _activityFeed?: TypeSpec<ActivityFeed>;

  static get $runtimeType(): ClassType<JsonContext> {
    return (JsonContext$rt ??= {
      kind: "class",
      name: "JsonContext",
      fullName: "SampleIssueTracker.JsonContext",
      namespace: "SampleIssueTracker",
      ctor: JsonContext,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new JsonContext(),
      test: (v: unknown): v is JsonContext => v instanceof JsonContext,
    });
  }

  static get default(): JsonContext {
    return JsonContext._default;
  }

  get issueSnapshot(): TypeSpec<IssueSnapshot> {
    return (this._issueSnapshot ??= this.createSpec({
      type: IssueSnapshot,
      runtimeType: () => IssueSnapshot.$runtimeType,
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
      runtimeType: () => Comment.$runtimeType,
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

  get iReadOnlyListComment(): ValueSpec<IReadOnlyList<Comment> | null> {
    return (this._iReadOnlyListComment ??= this.createValueSpec({
      descriptor: {
        kind: "list",
        element: {
          kind: "ref",
          spec: () => this.comment,
        },
        create: (items) => List.of(items),
      },
    }));
  }

  get issueActivity(): TypeSpec<IssueActivity> {
    return (this._issueActivity ??= this.createSpec({
      type: IssueActivity,
      runtimeType: () => IssueActivity.$runtimeType,
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
      runtimeType: () => ActivityFeed.$runtimeType,
      factory: (p: Record<string, unknown>) =>
        new ActivityFeed(p.entries as IReadOnlyDictionary<bigint, IssueActivity>),
      properties: [
        {
          ts: "entries",
          json: "entries",
          type: {
            kind: "dictionary",
            key: { kind: "int64", numberType: "System.Int64" },
            value: {
              kind: "ref",
              spec: () => this.issueActivity,
            },
            create: (entries) => Dictionary.assign(entries),
          },
        },
      ],
    }));
  }
}
