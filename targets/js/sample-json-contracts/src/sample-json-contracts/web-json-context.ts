/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Dictionary, List, type IReadOnlyList } from "metano-runtime/system/collections";
import type { ClassType } from "metano-runtime";
import { Decimal } from "metano-runtime/system/decimal";
import {
  SerializerContext,
  type JsonReadOptions,
  type TypeSpec,
  type ValueSpec,
} from "metano-runtime/system/json";
import { Clip } from "./clip.js";
import { Color } from "./color.js";
import { Group } from "./group.js";
import { IBase } from "./i-base.js";
import { IA } from "./ia.js";
import { ImageClip } from "./image-clip.js";
import { IS } from "./is.js";
import { Nums } from "./nums.js";
import { SilenceClip } from "./silence-clip.js";
import { SoftNums } from "./soft-nums.js";

let WebJsonContext$rt: ClassType<WebJsonContext> | undefined;

export class WebJsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new WebJsonContext() as WebJsonContext;
  private _clip?: TypeSpec<Clip>;
  private _imageClip?: TypeSpec<ImageClip>;
  private _iBase?: TypeSpec<IBase>;
  private _nums?: TypeSpec<Nums>;
  private _softNums?: TypeSpec<SoftNums>;
  private _int32?: ValueSpec<number>;
  private _silenceClip?: TypeSpec<SilenceClip>;
  private _group?: TypeSpec<Group>;
  private _iA?: TypeSpec<IA>;
  private _iS?: TypeSpec<IS>;

  static get $runtimeType(): ClassType<WebJsonContext> {
    return (WebJsonContext$rt ??= {
      kind: "class",
      name: "WebJsonContext",
      fullName: "SampleJsonContracts.WebJsonContext",
      namespace: "SampleJsonContracts",
      ctor: WebJsonContext,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new WebJsonContext(),
      test: (v: unknown): v is WebJsonContext => v instanceof WebJsonContext,
    });
  }

  static get default(): WebJsonContext {
    return WebJsonContext._default;
  }

  protected override get readOptions(): JsonReadOptions {
    return { propertyNameCaseInsensitive: true, numberHandling: "allowReadingFromString" };
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
          json: "seconds",
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
          json: "path",
          type: { kind: "primitive" },
        },
      ],
    }));
  }

  get iBase(): TypeSpec<IBase> {
    return (this._iBase ??= this.createSpec({
      type: IBase,
      runtimeType: () => IBase.$runtimeType,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'IBase' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: 1,
            spec: () => this.iA,
          },
          {
            tag: "s",
            spec: () => this.iS,
          },
        ],
      },
    }));
  }

  get nums(): TypeSpec<Nums> {
    return (this._nums ??= this.createSpec({
      type: Nums,
      runtimeType: () => Nums.$runtimeType,
      factory: (p: Record<string, unknown>) =>
        new Nums(
          p.n as number,
          p.m as number | null,
          p.d as Decimal,
          p.x as number,
          p.l as List<number>,
          p.map as Dictionary<string, number>,
          p.c as Color,
        ),
      properties: [
        {
          ts: "n",
          json: "n",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "m",
          json: "m",
          type: {
            kind: "nullable",
            inner: { kind: "primitive", numberType: "System.Int32" },
          },
          optional: true,
        },
        {
          ts: "d",
          json: "d",
          type: { kind: "decimal", type: Decimal, typeName: "System.Decimal" },
        },
        {
          ts: "x",
          json: "x",
          type: { kind: "primitive", numberType: "System.Double" },
        },
        {
          ts: "l",
          json: "l",
          type: {
            kind: "list",
            element: { kind: "primitive", numberType: "System.Int32" },
            create: (items) => List.of(items),
          },
        },
        {
          ts: "map",
          json: "map",
          type: {
            kind: "dictionary",
            key: { kind: "primitive" },
            value: { kind: "primitive", numberType: "System.Int32" },
            create: (entries) => Dictionary.assign(entries),
          },
        },
        {
          ts: "c",
          json: "c",
          type: { kind: "numericEnum", values: Color, typeName: "SampleJsonContracts.Color" },
        },
      ],
    }));
  }

  get softNums(): TypeSpec<SoftNums> {
    return (this._softNums ??= this.createSpec({
      type: SoftNums,
      runtimeType: () => SoftNums.$runtimeType,
      factory: (p: Record<string, unknown>) =>
        new SoftNums(
          p.n as number | null,
          p.d as Decimal | null,
          p.x as number | null,
          p.c as Color | null,
        ),
      properties: [
        {
          ts: "n",
          json: "n",
          type: {
            kind: "nullable",
            inner: { kind: "primitive", numberType: "System.Int32" },
          },
          optional: true,
        },
        {
          ts: "d",
          json: "d",
          type: {
            kind: "nullable",
            inner: { kind: "decimal", type: Decimal, typeName: "System.Decimal" },
          },
          optional: true,
        },
        {
          ts: "x",
          json: "x",
          type: {
            kind: "nullable",
            inner: { kind: "primitive", numberType: "System.Double" },
          },
          optional: true,
        },
        {
          ts: "c",
          json: "c",
          type: {
            kind: "nullable",
            inner: { kind: "numericEnum", values: Color, typeName: "SampleJsonContracts.Color" },
          },
          optional: true,
        },
      ],
    }));
  }

  get int32(): ValueSpec<number> {
    return (this._int32 ??= this.createValueSpec({
      descriptor: { kind: "primitive", numberType: "System.Int32" },
      valueType: true,
      typeName: "System.Int32",
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
          json: "name",
          type: { kind: "primitive" },
        },
        {
          ts: "members",
          json: "members",
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

  get iA(): TypeSpec<IA> {
    return (this._iA ??= this.createSpec({
      type: IA,
      runtimeType: () => IA.$runtimeType,
      base: this.iBase,
      factory: (p: Record<string, unknown>) => new IA(p.x as number),
      properties: [
        {
          ts: "x",
          json: "x",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get iS(): TypeSpec<IS> {
    return (this._iS ??= this.createSpec({
      type: IS,
      runtimeType: () => IS.$runtimeType,
      base: this.iBase,
      factory: (p: Record<string, unknown>) => new IS(p.y as number),
      properties: [
        {
          ts: "y",
          json: "y",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }
}
