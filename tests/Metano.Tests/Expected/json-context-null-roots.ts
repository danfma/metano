import { Temporal } from "@js-temporal/polyfill";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Corner } from "./corner";
import { Level } from "./level";
import { Outer } from "./outer";
import { Pair } from "./pair";
import { SPoint } from "./s-point";
import { Segment } from "./segment";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _sPoint?: TypeSpec<SPoint>;

  private _nullableSPoint?: ValueSpec<SPoint | null>;

  private _segment?: TypeSpec<Segment>;

  private _boolean?: ValueSpec<boolean>;

  private _dateOnly?: ValueSpec<Temporal.PlainDate>;

  private _nullableBoolean?: ValueSpec<boolean | null>;

  private _level?: ValueSpec<Level>;

  private _inner?: TypeSpec<Outer.Inner>;

  private _pairInt32?: TypeSpec<Pair<number>>;

  private _corner?: TypeSpec<Corner>;

  static get default(): JsonContext {
    return this._default;
  }

  get sPoint(): TypeSpec<SPoint> {
    return this._sPoint ??= this.createSpec({
      type: SPoint,
      factory: (p: Record<string, unknown>) => new SPoint(p.x as number, p.y as number),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "y",
          json: "Y",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      valueType: true,
      typeName: "TestApp.SPoint",
    });
  }

  get nullableSPoint(): ValueSpec<SPoint | null> {
    return this._nullableSPoint ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: {
          kind: "ref",
          spec: () => this.sPoint,
        },
      },
    });
  }

  get segment(): TypeSpec<Segment> {
    return this._segment ??= this.createSpec({
      type: Segment,
      factory: (p: Record<string, unknown>) => new Segment(p.from as SPoint, p.to as SPoint | null, p.tip as Corner),
      properties: [
        {
          ts: "from",
          json: "From",
          type: {
            kind: "ref",
            spec: () => this.sPoint,
          },
        },
        {
          ts: "to",
          json: "To",
          type: {
            kind: "nullable",
            inner: {
              kind: "ref",
              spec: () => this.sPoint,
            },
          },
          optional: true,
        },
        {
          ts: "tip",
          json: "Tip",
          type: {
            kind: "ref",
            spec: () => this.corner,
          },
        },
      ],
    });
  }

  get boolean(): ValueSpec<boolean> {
    return this._boolean ??= this.createValueSpec({
      descriptor: { kind: "primitive", typeName: "System.Boolean" },
      valueType: true,
      typeName: "System.Boolean",
    });
  }

  get dateOnly(): ValueSpec<Temporal.PlainDate> {
    return this._dateOnly ??= this.createValueSpec({
      descriptor: { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      valueType: true,
      typeName: "System.DateOnly",
    });
  }

  get nullableBoolean(): ValueSpec<boolean | null> {
    return this._nullableBoolean ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "primitive", typeName: "System.Boolean" },
      },
    });
  }

  get level(): ValueSpec<Level> {
    return this._level ??= this.createValueSpec({
      descriptor: { kind: "numericEnum", values: Level, typeName: "TestApp.Level" },
      valueType: true,
      typeName: "TestApp.Level",
    });
  }

  get inner(): TypeSpec<Outer.Inner> {
    return this._inner ??= this.createSpec({
      type: Outer.Inner,
      factory: (p: Record<string, unknown>) => new Outer.Inner(p.v as number),
      properties: [
        {
          ts: "v",
          json: "V",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      valueType: true,
      typeName: "TestApp.Outer+Inner",
    });
  }

  get pairInt32(): TypeSpec<Pair<number>> {
    return this._pairInt32 ??= this.createSpec<Pair<number>>({
      type: Pair,
      factory: (p: Record<string, unknown>) => new Pair(p.a as number),
      properties: [
        {
          ts: "a",
          json: "A",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      valueType: true,
      typeName: "TestApp.Pair`1[System.Int32]",
    });
  }

  get corner(): TypeSpec<Corner> {
    return this._corner ??= this.createSpec({
      type: Corner,
      factory: (p: Record<string, unknown>) => new Corner(p.x as number),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      valueType: true,
      typeName: "TestApp.Corner",
    });
  }
}
