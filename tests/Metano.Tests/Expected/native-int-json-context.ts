import { Dictionary } from "metano-runtime/system/collections";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Handles } from "./handles.js";
import { NativeId } from "./native-id.js";
import { NativeQuota } from "./native-quota.js";

export class JsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new JsonContext() as JsonContext;
  private _handles?: TypeSpec<Handles>;
  private _nativeId?: ValueSpec<NativeId>;
  private _nativeQuota?: ValueSpec<NativeQuota>;
  private _intPtr?: ValueSpec<bigint>;
  private _nullableUIntPtr?: ValueSpec<bigint | null>;

  static get default(): JsonContext {
    return this._default;
  }

  get handles(): TypeSpec<Handles> {
    return this._handles ??= this.createSpec({
      type: Handles,
      runtimeType: () => Handles.$runtimeType,
      factory: (p: Record<string, unknown>) => new Handles(p.signed as bigint, p.unsigned as bigint, p.parent as bigint | null, p.values as bigint[], p.byHandle as Dictionary<bigint, string>, p.counts as Dictionary<string, bigint>, p.id as NativeId, p.byToken as Dictionary<NativeId, string>, p.quota as NativeQuota),
      properties: [
        {
          ts: "signed",
          json: "Signed",
          type: { kind: "int64", numberType: "System.Int64" },
        },
        {
          ts: "unsigned",
          json: "Unsigned",
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
          ts: "values",
          json: "Values",
          type: {
            kind: "array",
            element: { kind: "int64", numberType: "System.UInt64" },
          },
        },
        {
          ts: "byHandle",
          json: "ByHandle",
          type: {
            kind: "dictionary",
            key: { kind: "int64", numberType: "System.Int64" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "counts",
          json: "Counts",
          type: {
            kind: "dictionary",
            key: { kind: "primitive" },
            value: { kind: "int64", numberType: "System.UInt64" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "id",
          json: "Id",
          type: {
            kind: "int64",
            numberType: "System.Int64",
            typeName: "TestApp.NativeId",
            create: NativeId.create,
          },
        },
        {
          ts: "byToken",
          json: "ByToken",
          type: {
            kind: "dictionary",
            key: {
              kind: "int64",
              numberType: "System.Int64",
              typeName: "TestApp.NativeId",
              create: NativeId.create,
            },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "quota",
          json: "Quota",
          type: {
            kind: "int64",
            numberType: "System.UInt64",
            typeName: "TestApp.NativeQuota",
            create: NativeQuota.create,
          },
        },
      ],
    });
  }

  get nativeId(): ValueSpec<NativeId> {
    return this._nativeId ??= this.createValueSpec({
      descriptor: {
        kind: "int64",
        numberType: "System.Int64",
        typeName: "TestApp.NativeId",
        create: NativeId.create,
      },
      valueType: true,
      typeName: "TestApp.NativeId",
    });
  }

  get nativeQuota(): ValueSpec<NativeQuota> {
    return this._nativeQuota ??= this.createValueSpec({
      descriptor: {
        kind: "int64",
        numberType: "System.UInt64",
        typeName: "TestApp.NativeQuota",
        create: NativeQuota.create,
      },
      valueType: true,
      typeName: "TestApp.NativeQuota",
    });
  }

  get intPtr(): ValueSpec<bigint> {
    return this._intPtr ??= this.createValueSpec({
      descriptor: { kind: "int64", numberType: "System.Int64" },
      valueType: true,
      typeName: "System.IntPtr",
    });
  }

  get nullableUIntPtr(): ValueSpec<bigint | null> {
    return this._nullableUIntPtr ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "int64", numberType: "System.UInt64" },
      },
    });
  }
}
