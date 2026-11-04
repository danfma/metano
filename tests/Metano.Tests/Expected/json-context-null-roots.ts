import { Temporal } from "@js-temporal/polyfill";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Corner } from "./corner";
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
          type: { kind: "primitive" },
        },
        {
          ts: "y",
          json: "Y",
          type: { kind: "primitive" },
        },
      ],
      valueType: true,
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
      descriptor: { kind: "primitive" },
      valueType: true,
    });
  }

  get dateOnly(): ValueSpec<Temporal.PlainDate> {
    return this._dateOnly ??= this.createValueSpec({
      descriptor: { kind: "temporal", parse: Temporal.PlainDate.from },
      valueType: true,
    });
  }

  get nullableBoolean(): ValueSpec<boolean | null> {
    return this._nullableBoolean ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "primitive" },
      },
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
          type: { kind: "primitive" },
        },
      ],
      valueType: true,
    });
  }
}
