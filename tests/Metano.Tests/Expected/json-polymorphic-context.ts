import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { ALeaf } from "./a-leaf";
import { AMid } from "./a-mid";
import { AB } from "./ab";
import { Clip } from "./clip";
import { GBase } from "./g-base";
import { GDer } from "./g-der";
import { IgnA } from "./ign-a";
import { IgnBase } from "./ign-base";
import { ImageClip } from "./image-clip";
import { IntA } from "./int-a";
import { IntBase } from "./int-base";
import { IntS } from "./int-s";
import { SBase } from "./s-base";
import { SDer } from "./s-der";
import { SilenceClip } from "./silence-clip";
import { Timeline } from "./timeline";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _timeline?: TypeSpec<Timeline>;

  private _listClip?: ValueSpec<Clip[] | null>;

  private _intBase?: TypeSpec<IntBase>;

  private _sBase?: TypeSpec<SBase>;

  private _gBaseInt32?: TypeSpec<GBase<number>>;

  private _aB?: TypeSpec<AB>;

  private _ignBase?: TypeSpec<IgnBase>;

  private _clip?: TypeSpec<Clip>;

  private _intA?: TypeSpec<IntA>;

  private _intS?: TypeSpec<IntS>;

  private _sDer?: TypeSpec<SDer>;

  private _gDer?: TypeSpec<GDer>;

  private _aMid?: TypeSpec<AMid>;

  private _aLeaf?: TypeSpec<ALeaf>;

  private _ignA?: TypeSpec<IgnA>;

  private _imageClip?: TypeSpec<ImageClip>;

  private _silenceClip?: TypeSpec<SilenceClip>;

  static get default(): JsonContext {
    return this._default;
  }

  get timeline(): TypeSpec<Timeline> {
    return this._timeline ??= this.createSpec({
      type: Timeline,
      factory: (p: Record<string, unknown>) => new Timeline(p.name as string, p.main as Clip, p.clips as Clip[]),
      properties: [
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
        {
          ts: "main",
          json: "Main",
          type: {
            kind: "ref",
            spec: () => this.clip,
          },
        },
        {
          ts: "clips",
          json: "Clips",
          type: {
            kind: "array",
            element: {
              kind: "ref",
              spec: () => this.clip,
            },
          },
        },
      ],
    });
  }

  get listClip(): ValueSpec<Clip[] | null> {
    return this._listClip ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.clip,
        },
      },
    });
  }

  get intBase(): TypeSpec<IntBase> {
    return this._intBase ??= this.createSpec({
      type: IntBase,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'IntBase' directly; deserialize it through a polymorphic spec.");
      },
      properties: [],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: 1,
            spec: () => this.intA,
          },
          {
            tag: "s",
            spec: () => this.intS,
          },
        ],
      },
    });
  }

  get sBase(): TypeSpec<SBase> {
    return this._sBase ??= this.createSpec({
      type: SBase,
      factory: (p: Record<string, unknown>) => new SBase(p.v as number),
      properties: [
        {
          ts: "v",
          json: "V",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        derived: [
          {
            tag: "b",
            spec: () => this.sBase,
          },
          {
            tag: "d",
            spec: () => this.sDer,
          },
        ],
      },
    });
  }

  get gBaseInt32(): TypeSpec<GBase<number>> {
    return this._gBaseInt32 ??= this.createSpec<GBase<number>>({
      type: GBase,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'GBase' directly; deserialize it through a polymorphic spec.");
      },
      properties: [
        {
          ts: "value",
          json: "Value",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "g",
            spec: () => this.gDer,
          },
        ],
      },
    });
  }

  get aB(): TypeSpec<AB> {
    return this._aB ??= this.createSpec({
      type: AB,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'AB' directly; deserialize it through a polymorphic spec.");
      },
      properties: [
        {
          ts: "a",
          json: "A",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "m",
            spec: () => this.aMid,
            abstract: true,
          },
          {
            tag: "l",
            spec: () => this.aLeaf,
          },
        ],
        unknownDerivedType: "fallBackToNearestAncestor",
      },
    });
  }

  get ignBase(): TypeSpec<IgnBase> {
    return this._ignBase ??= this.createSpec({
      type: IgnBase,
      factory: (p: Record<string, unknown>) => new IgnBase(p.v as number),
      properties: [
        {
          ts: "v",
          json: "V",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        derived: [
          {
            tag: "a",
            spec: () => this.ignA,
          },
        ],
        unknownDerivedType: "fallBackToBaseType",
        ignoreUnrecognizedTags: true,
      },
    });
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
        ],
      },
    });
  }

  get intA(): TypeSpec<IntA> {
    return this._intA ??= this.createSpec({
      type: IntA,
      base: this.intBase,
      factory: (p: Record<string, unknown>) => new IntA(p.x as number),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get intS(): TypeSpec<IntS> {
    return this._intS ??= this.createSpec({
      type: IntS,
      base: this.intBase,
      factory: (p: Record<string, unknown>) => new IntS(p.y as number),
      properties: [
        {
          ts: "y",
          json: "Y",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get sDer(): TypeSpec<SDer> {
    return this._sDer ??= this.createSpec({
      type: SDer,
      base: this.sBase,
      factory: (p: Record<string, unknown>) => new SDer(p.v as number, p.w as number),
      properties: [
        {
          ts: "w",
          json: "W",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get gDer(): TypeSpec<GDer> {
    return this._gDer ??= this.createSpec({
      type: GDer,
      base: this.gBaseInt32,
      factory: (p: Record<string, unknown>) => new GDer(p.value as number, p.extra as number),
      properties: [
        {
          ts: "extra",
          json: "Extra",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get aMid(): TypeSpec<AMid> {
    return this._aMid ??= this.createSpec({
      type: AMid,
      base: this.aB,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'AMid': polymorphic JSON deserialization is not supported yet.");
      },
      properties: [
        {
          ts: "m",
          json: "M",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get aLeaf(): TypeSpec<ALeaf> {
    return this._aLeaf ??= this.createSpec({
      type: ALeaf,
      base: this.aMid,
      factory: (p: Record<string, unknown>) => new ALeaf(p.l as number, p.m as number, p.a as number),
      properties: [
        {
          ts: "l",
          json: "L",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get ignA(): TypeSpec<IgnA> {
    return this._ignA ??= this.createSpec({
      type: IgnA,
      base: this.ignBase,
      factory: (p: Record<string, unknown>) => new IgnA(p.v as number),
      properties: [],
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
          json: "Path",
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
}
