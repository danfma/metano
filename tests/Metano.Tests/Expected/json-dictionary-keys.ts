import { Temporal } from "@js-temporal/polyfill";
import { UUID } from "metano-runtime";
import { Dictionary } from "metano-runtime/system/collections";
import { Decimal } from "metano-runtime/system/decimal";
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Keys } from "./keys.js";
import { Level } from "./level.js";
import { Mood } from "./mood.js";
import { Shade } from "./shade.js";
import { Tag } from "./tag.js";

export class JsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new JsonContext() as JsonContext;
  private _keys?: TypeSpec<Keys>;

  static get default(): JsonContext {
    return this._default;
  }

  get keys(): TypeSpec<Keys> {
    return this._keys ??= this.createSpec({
      type: Keys,
      runtimeType: () => Keys.$runtimeType,
      factory: (p: Record<string, unknown>) => new Keys(p.byInt as Dictionary<number, string>, p.byLong as Dictionary<bigint, string>, p.byShort as Dictionary<number, string>, p.byByte as Dictionary<number, string>, p.byUInt as Dictionary<number, string>, p.byDouble as Dictionary<number, string>, p.byFlag as Dictionary<boolean, string>, p.byLevel as Dictionary<Level, string>, p.byShade as Dictionary<Shade, string>, p.byMood as Dictionary<Mood, string>, p.byId as Dictionary<UUID, string>, p.byDay as Dictionary<Temporal.PlainDate, string>, p.byPrice as Dictionary<Decimal, string>, p.byName as Dictionary<string, string>, p.byTag as Dictionary<Tag, string>),
      properties: [
        {
          ts: "byInt",
          json: "ByInt",
          type: {
            kind: "dictionary",
            key: { kind: "primitive", numberType: "System.Int32" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byLong",
          json: "ByLong",
          type: {
            kind: "dictionary",
            key: { kind: "int64", numberType: "System.Int64" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byShort",
          json: "ByShort",
          type: {
            kind: "dictionary",
            key: { kind: "primitive", numberType: "System.Int16" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byByte",
          json: "ByByte",
          type: {
            kind: "dictionary",
            key: { kind: "primitive", numberType: "System.Byte" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byUInt",
          json: "ByUInt",
          type: {
            kind: "dictionary",
            key: { kind: "primitive", numberType: "System.UInt32" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byDouble",
          json: "ByDouble",
          type: {
            kind: "dictionary",
            key: { kind: "primitive", numberType: "System.Double" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byFlag",
          json: "ByFlag",
          type: {
            kind: "dictionary",
            key: { kind: "primitive", typeName: "System.Boolean" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byLevel",
          json: "ByLevel",
          type: {
            kind: "dictionary",
            key: {
              kind: "numericEnum",
              values: Level,
              typeName: "TestApp.Level",
              numberType: "System.Int32",
            },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byShade",
          json: "ByShade",
          type: {
            kind: "dictionary",
            key: {
              kind: "numericEnum",
              values: Shade,
              typeName: "TestApp.Shade",
              numberType: "System.Byte",
            },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byMood",
          json: "ByMood",
          type: {
            kind: "dictionary",
            key: { kind: "enum", values: Mood, typeName: "TestApp.Mood" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byId",
          json: "ById",
          type: {
            kind: "dictionary",
            key: { kind: "branded", create: UUID.create, typeName: "System.Guid" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byDay",
          json: "ByDay",
          type: {
            kind: "dictionary",
            key: { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byPrice",
          json: "ByPrice",
          type: {
            kind: "dictionary",
            key: { kind: "decimal", type: Decimal, typeName: "System.Decimal" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byName",
          json: "ByName",
          type: {
            kind: "dictionary",
            key: { kind: "primitive" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "byTag",
          json: "ByTag",
          type: {
            kind: "dictionary",
            key: { kind: "branded", create: Tag.create, typeName: "TestApp.Tag" },
            value: { kind: "primitive" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
      ],
    });
  }
}
