import { HashSet } from "metano-runtime";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Boxes } from "./boxes";
import { EntityId } from "./entity-id";
import { Snowflake } from "./snowflake";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _snowflake?: TypeSpec<Snowflake>;

  private _boxes?: TypeSpec<Boxes>;

  private _entityId?: ValueSpec<EntityId>;

  private _int64?: ValueSpec<number>;

  private _nullableUInt64?: ValueSpec<number | null>;

  static get default(): JsonContext {
    return this._default;
  }

  get snowflake(): TypeSpec<Snowflake> {
    return this._snowflake ??= this.createSpec({
      type: Snowflake,
      factory: (p: Record<string, unknown>) => new Snowflake(p.id as number, p.mask as number, p.parent as number | null),
      properties: [
        {
          ts: "id",
          json: "Id",
          type: { kind: "primitive", numberType: "System.Int64" },
        },
        {
          ts: "mask",
          json: "Mask",
          type: { kind: "primitive", numberType: "System.UInt64" },
        },
        {
          ts: "parent",
          json: "Parent",
          type: {
            kind: "nullable",
            inner: { kind: "primitive", numberType: "System.Int64" },
          },
          optional: true,
        },
      ],
    });
  }

  get boxes(): TypeSpec<Boxes> {
    return this._boxes ??= this.createSpec({
      type: Boxes,
      factory: (p: Record<string, unknown>) => new Boxes(p.values as number[], p.more as number[], p.byName as Map<number, string>, p.counts as Map<string, number>, p.set as HashSet<number>, p.id as EntityId),
      properties: [
        {
          ts: "values",
          json: "Values",
          type: {
            kind: "array",
            element: { kind: "primitive", numberType: "System.Int64" },
          },
        },
        {
          ts: "more",
          json: "More",
          type: {
            kind: "array",
            element: { kind: "primitive", numberType: "System.UInt64" },
          },
        },
        {
          ts: "byName",
          json: "ByName",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.Int64" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "counts",
          json: "Counts",
          type: {
            kind: "map",
            key: { kind: "primitive" },
            value: { kind: "primitive", numberType: "System.UInt64" },
          },
        },
        {
          ts: "set",
          json: "Set",
          type: {
            kind: "hashSet",
            element: { kind: "primitive", numberType: "System.Int64" },
          },
        },
        {
          ts: "id",
          json: "Id",
          type: { kind: "branded", create: EntityId.create, typeName: "TestApp.EntityId" },
        },
      ],
    });
  }

  get entityId(): ValueSpec<EntityId> {
    return this._entityId ??= this.createValueSpec({
      descriptor: { kind: "branded", create: EntityId.create, typeName: "TestApp.EntityId" },
      valueType: true,
      typeName: "TestApp.EntityId",
    });
  }

  get int64(): ValueSpec<number> {
    return this._int64 ??= this.createValueSpec({
      descriptor: { kind: "primitive", numberType: "System.Int64" },
      valueType: true,
      typeName: "System.Int64",
    });
  }

  get nullableUInt64(): ValueSpec<number | null> {
    return this._nullableUInt64 ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "primitive", numberType: "System.UInt64" },
      },
    });
  }
}
