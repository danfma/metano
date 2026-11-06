import { HashSet } from "metano-runtime";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Boxes } from "./boxes";
import { EntityId } from "./entity-id";
import { Quota } from "./quota";
import { Snowflake } from "./snowflake";
import { TokenId } from "./token-id";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _snowflake?: TypeSpec<Snowflake>;

  private _boxes?: TypeSpec<Boxes>;

  private _entityId?: ValueSpec<EntityId>;

  private _quota?: ValueSpec<Quota>;

  private _tokenId?: ValueSpec<TokenId>;

  private _int64?: ValueSpec<bigint>;

  private _nullableUInt64?: ValueSpec<bigint | null>;

  static get default(): JsonContext {
    return this._default;
  }

  get snowflake(): TypeSpec<Snowflake> {
    return this._snowflake ??= this.createSpec({
      type: Snowflake,
      factory: (p: Record<string, unknown>) => new Snowflake(p.id as bigint, p.mask as bigint, p.parent as bigint | null, p.origin as EntityId | null),
      properties: [
        {
          ts: "id",
          json: "Id",
          type: { kind: "int64", numberType: "System.Int64" },
        },
        {
          ts: "mask",
          json: "Mask",
          type: { kind: "int64", numberType: "System.UInt64" },
        },
        {
          ts: "parent",
          json: "Parent",
          type: {
            kind: "nullable",
            inner: { kind: "int64", numberType: "System.Int64" },
          },
          optional: true,
        },
        {
          ts: "origin",
          json: "Origin",
          type: {
            kind: "nullable",
            inner: {
              kind: "int64",
              numberType: "System.Int64",
              typeName: "TestApp.EntityId",
              create: EntityId.create,
            },
          },
          optional: true,
        },
      ],
    });
  }

  get boxes(): TypeSpec<Boxes> {
    return this._boxes ??= this.createSpec({
      type: Boxes,
      factory: (p: Record<string, unknown>) => new Boxes(p.values as bigint[], p.more as bigint[], p.byName as Map<bigint, string>, p.counts as Map<string, bigint>, p.set as HashSet<bigint>, p.id as EntityId, p.byToken as Map<EntityId, string>, p.token as TokenId),
      properties: [
        {
          ts: "values",
          json: "Values",
          type: {
            kind: "array",
            element: { kind: "int64", numberType: "System.Int64" },
          },
        },
        {
          ts: "more",
          json: "More",
          type: {
            kind: "array",
            element: { kind: "int64", numberType: "System.UInt64" },
          },
        },
        {
          ts: "byName",
          json: "ByName",
          type: {
            kind: "map",
            key: { kind: "int64", numberType: "System.Int64" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "counts",
          json: "Counts",
          type: {
            kind: "map",
            key: { kind: "primitive" },
            value: { kind: "int64", numberType: "System.UInt64" },
          },
        },
        {
          ts: "set",
          json: "Set",
          type: {
            kind: "hashSet",
            element: { kind: "int64", numberType: "System.Int64" },
          },
        },
        {
          ts: "id",
          json: "Id",
          type: {
            kind: "int64",
            numberType: "System.Int64",
            typeName: "TestApp.EntityId",
            create: EntityId.create,
          },
        },
        {
          ts: "byToken",
          json: "ByToken",
          type: {
            kind: "map",
            key: {
              kind: "int64",
              numberType: "System.Int64",
              typeName: "TestApp.EntityId",
              create: EntityId.create,
            },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "token",
          json: "Token",
          type: {
            kind: "int64",
            numberType: "System.Int64",
            typeName: "TestApp.TokenId",
            create: TokenId.from,
          },
        },
      ],
    });
  }

  get entityId(): ValueSpec<EntityId> {
    return this._entityId ??= this.createValueSpec({
      descriptor: {
        kind: "int64",
        numberType: "System.Int64",
        typeName: "TestApp.EntityId",
        create: EntityId.create,
      },
      valueType: true,
      typeName: "TestApp.EntityId",
    });
  }

  get quota(): ValueSpec<Quota> {
    return this._quota ??= this.createValueSpec({
      descriptor: {
        kind: "int64",
        numberType: "System.Int64",
        typeName: "TestApp.Quota",
        create: Quota.create,
      },
      valueType: true,
      typeName: "TestApp.Quota",
    });
  }

  get tokenId(): ValueSpec<TokenId> {
    return this._tokenId ??= this.createValueSpec({
      descriptor: {
        kind: "int64",
        numberType: "System.Int64",
        typeName: "TestApp.TokenId",
        create: TokenId.from,
      },
      valueType: true,
      typeName: "TestApp.TokenId",
    });
  }

  get int64(): ValueSpec<bigint> {
    return this._int64 ??= this.createValueSpec({
      descriptor: { kind: "int64", numberType: "System.Int64" },
      valueType: true,
      typeName: "System.Int64",
    });
  }

  get nullableUInt64(): ValueSpec<bigint | null> {
    return this._nullableUInt64 ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "int64", numberType: "System.UInt64" },
      },
    });
  }
}
