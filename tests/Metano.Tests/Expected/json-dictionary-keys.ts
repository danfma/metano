import { Temporal } from "@js-temporal/polyfill";
import { UUID } from "metano-runtime";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Decimal } from "decimal.js";
import { Keys } from "./keys";
import { Level } from "./level";
import { Mood } from "./mood";
import { Shade } from "./shade";
import { Tag } from "./tag";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _keys?: TypeSpec<Keys>;

  private _dateOnly?: ValueSpec<Temporal.PlainDate>;

  static get default(): JsonContext {
    return this._default;
  }

  get keys(): TypeSpec<Keys> {
    return this._keys ??= this.createSpec({
      type: Keys,
      factory: (p: Record<string, unknown>) => new Keys(p.byInt as Map<number, string>, p.byLong as Map<number, string>, p.byShort as Map<number, string>, p.byByte as Map<number, string>, p.byUInt as Map<number, string>, p.byDouble as Map<number, string>, p.byFlag as Map<boolean, string>, p.byLevel as Map<Level, string>, p.byShade as Map<Shade, string>, p.byMood as Map<Mood, string>, p.byId as Map<UUID, string>, p.byDay as Map<Temporal.PlainDate, string>, p.byPrice as Map<Decimal, string>, p.byName as Map<string, string>, p.byTag as Map<Tag, string>),
      properties: [
        {
          ts: "byInt",
          json: "ByInt",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.Int32" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byLong",
          json: "ByLong",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.Int64" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byShort",
          json: "ByShort",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.Int16" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byByte",
          json: "ByByte",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.Byte" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byUInt",
          json: "ByUInt",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.UInt32" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byDouble",
          json: "ByDouble",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.Double" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byFlag",
          json: "ByFlag",
          type: {
            kind: "map",
            key: { kind: "primitive", typeName: "System.Boolean" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byLevel",
          json: "ByLevel",
          type: {
            kind: "map",
            key: {
              kind: "numericEnum",
              values: Level,
              numberType: "System.Int32",
              typeName: "TestApp.Level",
            },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byShade",
          json: "ByShade",
          type: {
            kind: "map",
            key: {
              kind: "numericEnum",
              values: Shade,
              numberType: "System.Byte",
              typeName: "TestApp.Shade",
            },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byMood",
          json: "ByMood",
          type: {
            kind: "map",
            key: { kind: "enum", values: Mood, typeName: "TestApp.Mood" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byId",
          json: "ById",
          type: {
            kind: "map",
            key: { kind: "branded", create: UUID.create, typeName: "System.Guid" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byDay",
          json: "ByDay",
          type: {
            kind: "map",
            key: { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byPrice",
          json: "ByPrice",
          type: {
            kind: "map",
            key: { kind: "decimal", type: Decimal },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byName",
          json: "ByName",
          type: {
            kind: "map",
            key: { kind: "primitive" },
            value: { kind: "primitive" },
          },
        },
        {
          ts: "byTag",
          json: "ByTag",
          type: {
            kind: "map",
            key: { kind: "branded", create: Tag.create },
            value: { kind: "primitive" },
          },
        },
      ],
    });
  }

  get dateOnly(): ValueSpec<Temporal.PlainDate> {
    return this._dateOnly ??= this.createValueSpec({
      descriptor: { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      valueType: true,
      typeName: "System.DateOnly",
    });
  }
}
