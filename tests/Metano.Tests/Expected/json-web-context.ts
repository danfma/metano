import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Clip } from "./clip";
import { ImageClip } from "./image-clip";
import { Shape } from "./shape";
import { SilenceClip } from "./silence-clip";
import { Square } from "./square";
import { Stats } from "./stats";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _clip?: TypeSpec<Clip>;

  private _shape?: TypeSpec<Shape>;

  private _stats?: TypeSpec<Stats>;

  private _imageClip?: TypeSpec<ImageClip>;

  private _silenceClip?: TypeSpec<SilenceClip>;

  private _square?: TypeSpec<Square>;

  static get default(): JsonContext {
    return this._default;
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
          type: { kind: "primitive" },
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
          type: { kind: "primitive" },
        },
        {
          ts: "ratio",
          json: "ratio",
          type: { kind: "primitive" },
        },
        {
          ts: "url",
          json: "URL",
          type: { kind: "primitive" },
        },
      ],
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
          type: { kind: "primitive" },
        },
      ],
    });
  }
}
