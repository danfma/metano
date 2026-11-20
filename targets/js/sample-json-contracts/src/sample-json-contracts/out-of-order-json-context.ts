/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { List, type IReadOnlyList } from "metano-runtime/system/collections";
import type { ClassType } from "metano-runtime";
import { SerializerContext, type JsonReadOptions, type TypeSpec } from "metano-runtime/system/json";
import { Clip } from "./clip";
import { Group } from "./group";
import { ImageClip } from "./image-clip";
import { SilenceClip } from "./silence-clip";

let OutOfOrderJsonContext$rt: ClassType<OutOfOrderJsonContext> | undefined;

export class OutOfOrderJsonContext extends SerializerContext {
  private static readonly _default =
    /* @__PURE__ */ new OutOfOrderJsonContext() as OutOfOrderJsonContext;
  private _clip?: TypeSpec<Clip>;
  private _imageClip?: TypeSpec<ImageClip>;
  private _silenceClip?: TypeSpec<SilenceClip>;
  private _group?: TypeSpec<Group>;

  static get $runtimeType(): ClassType<OutOfOrderJsonContext> {
    return (OutOfOrderJsonContext$rt ??= {
      kind: "class",
      name: "OutOfOrderJsonContext",
      fullName: "SampleJsonContracts.OutOfOrderJsonContext",
      namespace: "SampleJsonContracts",
      ctor: OutOfOrderJsonContext,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new OutOfOrderJsonContext(),
      test: (v: unknown): v is OutOfOrderJsonContext => v instanceof OutOfOrderJsonContext,
    });
  }

  static get default(): OutOfOrderJsonContext {
    return OutOfOrderJsonContext._default;
  }

  protected override get readOptions(): JsonReadOptions {
    return { allowOutOfOrderMetadataProperties: true };
  }

  get clip(): TypeSpec<Clip> {
    return (this._clip ??= this.createSpec({
      type: Clip,
      runtimeType: () => Clip.$runtimeType,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'Clip' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "seconds",
          json: "Seconds",
          type: { kind: "primitive", numberType: "System.Double" },
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
      runtimeType: () => ImageClip.$runtimeType,
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
      runtimeType: () => SilenceClip.$runtimeType,
      base: this.clip,
      factory: (p: Record<string, unknown>) => new SilenceClip(p.seconds as number),
      properties: [],
    }));
  }

  get group(): TypeSpec<Group> {
    return (this._group ??= this.createSpec({
      type: Group,
      runtimeType: () => Group.$runtimeType,
      base: this.clip,
      factory: (p: Record<string, unknown>) =>
        Object.assign(new Group(p.name as string, p.members as IReadOnlyList<Clip>), {
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
            kind: "list",
            element: {
              kind: "ref",
              spec: () => this.clip,
            },
            create: (items) => List.of(items),
          },
        },
      ],
    }));
  }
}
