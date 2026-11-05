import { SerializerContext, type JsonReadOptions, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Decimal } from "decimal.js";
import { Clip } from "./clip";
import { ImageClip } from "./image-clip";
import { Level } from "./level";
import { Reading } from "./reading";
import { Shape } from "./shape";
import { SilenceClip } from "./silence-clip";
import { Square } from "./square";
import { Stats } from "./stats";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _clip?: TypeSpec<Clip>;

  private _shape?: TypeSpec<Shape>;

  private _stats?: TypeSpec<Stats>;

  private _reading?: TypeSpec<Reading>;

  private _listUInt32?: ValueSpec<number[] | null>;

  private _nullableInt16?: ValueSpec<number | null>;

  private _imageClip?: TypeSpec<ImageClip>;

  private _silenceClip?: TypeSpec<SilenceClip>;

  private _square?: TypeSpec<Square>;

  static get default(): JsonContext {
    return this._default;
  }

  protected override get readOptions(): JsonReadOptions {
    return { propertyNameCaseInsensitive: true, numberHandling: "allowReadingFromString" };
  }

  get clip(): TypeSpec<Clip> {
    return this._clip ??= this.createSpec({
      type: Clip,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'Clip' directly; deserialize it through a polymorphic spec.");
      },
      properties: [
        {
          ts: "seconds",
          json: "seconds",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
      polymorphism: {
        discriminator: "Kind",
        abstract: true,
        derived: [
          {
            tag: "image",
            spec: () => this.imageClip,
          },
          {
            tag: "silence",
            spec: () => this.silenceClip,
          },
        ],
      },
    });
  }

  get shape(): TypeSpec<Shape> {
    return this._shape ??= this.createSpec({
      type: Shape,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'Shape' directly; deserialize it through a polymorphic spec.");
      },
      properties: [
        {
          ts: "fillColor",
          json: "fillColor",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "square",
            spec: () => this.square,
          },
        ],
      },
    });
  }

  get stats(): TypeSpec<Stats> {
    return this._stats ??= this.createSpec({
      type: Stats,
      factory: (p: Record<string, unknown>) => new Stats(p.count as number, p.ratio as number, p.url as string),
      properties: [
        {
          ts: "count",
          json: "count",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "ratio",
          json: "ratio",
          type: { kind: "primitive", numberType: "System.Double" },
        },
        {
          ts: "url",
          json: "URL",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get reading(): TypeSpec<Reading> {
    return this._reading ??= this.createSpec({
      type: Reading,
      factory: (p: Record<string, unknown>) => new Reading(p.amount as Decimal, p.total as number | null, p.weight as number, p.samples as number[], p.offsets as Map<number, number>, p.level as Level, p.valid as boolean),
      properties: [
        {
          ts: "amount",
          json: "amount",
          type: { kind: "decimal", type: Decimal, typeName: "System.Decimal" },
        },
        {
          ts: "total",
          json: "total",
          type: {
            kind: "nullable",
            inner: { kind: "primitive", numberType: "System.Int64" },
          },
          optional: true,
        },
        {
          ts: "weight",
          json: "weight",
          type: { kind: "primitive", numberType: "System.Single" },
        },
        {
          ts: "samples",
          json: "samples",
          type: {
            kind: "array",
            element: { kind: "primitive", numberType: "System.Byte" },
          },
        },
        {
          ts: "offsets",
          json: "offsets",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.UInt16" },
            value: { kind: "primitive", numberType: "System.SByte" },
          },
        },
        {
          ts: "level",
          json: "level",
          type: { kind: "numericEnum", values: Level, typeName: "TestApp.Level" },
        },
        {
          ts: "valid",
          json: "valid",
          type: { kind: "primitive", typeName: "System.Boolean" },
        },
      ],
    });
  }

  get listUInt32(): ValueSpec<number[] | null> {
    return this._listUInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "primitive", numberType: "System.UInt32" },
      },
    });
  }

  get nullableInt16(): ValueSpec<number | null> {
    return this._nullableInt16 ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "primitive", numberType: "System.Int16" },
      },
    });
  }

  get imageClip(): TypeSpec<ImageClip> {
    return this._imageClip ??= this.createSpec({
      type: ImageClip,
      base: this.clip,
      factory: (p: Record<string, unknown>) => new ImageClip(p.path as string, p.seconds as number),
      properties: [
        {
          ts: "path",
          json: "path",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get silenceClip(): TypeSpec<SilenceClip> {
    return this._silenceClip ??= this.createSpec({
      type: SilenceClip,
      base: this.clip,
      factory: (p: Record<string, unknown>) => new SilenceClip(p.seconds as number),
      properties: [],
    });
  }

  get square(): TypeSpec<Square> {
    return this._square ??= this.createSpec({
      type: Square,
      base: this.shape,
      factory: (p: Record<string, unknown>) => new Square(p.sideLength as number, p.fillColor as string),
      properties: [
        {
          ts: "sideLength",
          json: "sideLength",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    });
  }
}
