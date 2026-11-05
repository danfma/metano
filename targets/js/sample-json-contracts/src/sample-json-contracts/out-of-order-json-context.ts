/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { SerializerContext, type JsonReadOptions, type TypeSpec } from "metano-runtime/system/json";
import { Clip } from "./clip";
import { Group } from "./group";
import { ImageClip } from "./image-clip";
import { SilenceClip } from "./silence-clip";

export class OutOfOrderJsonContext extends SerializerContext {
  private static readonly _default: OutOfOrderJsonContext = new OutOfOrderJsonContext();

  private _clip?: TypeSpec<Clip>;

  private _imageClip?: TypeSpec<ImageClip>;

  private _silenceClip?: TypeSpec<SilenceClip>;

  private _group?: TypeSpec<Group>;

  static get default(): OutOfOrderJsonContext {
    return OutOfOrderJsonContext._default;
  }

  protected override get readOptions(): JsonReadOptions {
    return { allowOutOfOrderMetadataProperties: true };
  }

  get clip(): TypeSpec<Clip> {
    return (this._clip ??= this.createSpec({
      type: Clip,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'Clip' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "seconds",
          json: "Seconds",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "kind",
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
          {
            tag: "group",
            spec: () => this.group,
          },
        ],
      },
    }));
  }

  get imageClip(): TypeSpec<ImageClip> {
    return (this._imageClip ??= this.createSpec({
      type: ImageClip,
      base: this.clip,
      factory: (p: Record<string, unknown>) => new ImageClip(p.path as string, p.seconds as number),
      properties: [
        {
          ts: "path",
          json: "Path",
          type: { kind: "primitive" },
        },
      ],
    }));
  }

  get silenceClip(): TypeSpec<SilenceClip> {
    return (this._silenceClip ??= this.createSpec({
      type: SilenceClip,
      base: this.clip,
      factory: (p: Record<string, unknown>) => new SilenceClip(p.seconds as number),
      properties: [],
    }));
  }

  get group(): TypeSpec<Group> {
    return (this._group ??= this.createSpec({
      type: Group,
      base: this.clip,
      factory: (p: Record<string, unknown>) =>
        Object.assign(new Group(p.name as string, p.members as Clip[]), {
          seconds: p.seconds as number,
        }),
      properties: [
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
        {
          ts: "members",
          json: "Members",
          type: {
            kind: "array",
            element: {
              kind: "ref",
              spec: () => this.clip,
            },
          },
        },
      ],
    }));
  }
}
