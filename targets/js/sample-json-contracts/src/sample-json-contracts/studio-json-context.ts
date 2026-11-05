/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Temporal } from "@js-temporal/polyfill";
import { type HashSet, UUID } from "metano-runtime";
import { Decimal } from "decimal.js";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Clip } from "./clip";
import { Color } from "./color";
import { DBase } from "./d-base";
import { DBer } from "./d-ber";
import { EpisodeRec } from "./episode-rec";
import { EpisodeSummary } from "./episode-summary";
import { Feed } from "./feed";
import { Flavor } from "./flavor";
import { GBase } from "./g-base";
import { GDer } from "./g-der";
import { Group } from "./group";
import { GuidMapHolder } from "./guid-map-holder";
import { HBase } from "./h-base";
import { HLeaf } from "./h-leaf";
import { HMid } from "./h-mid";
import { HolderChar } from "./holder-char";
import { HolderInt } from "./holder-int";
import { HolderLevel } from "./holder-level";
import { HolderSPoint } from "./holder-s-point";
import { IBase } from "./i-base";
import type { IShape } from "./i-shape";
import type { IShapeB } from "./i-shape-b";
import { IA } from "./ia";
import { ImageClip } from "./image-clip";
import { IntMapHolder } from "./int-map-holder";
import { IS } from "./is";
import { Level } from "./level";
import { LevelMapHolder } from "./level-map-holder";
import { MixedBase } from "./mixed-base";
import { MixedClass } from "./mixed-class";
import { isMixedPoint, type MixedPoint } from "./mixed-point";
import { NBase } from "./n-base";
import { NLeaf } from "./n-leaf";
import { NMid } from "./n-mid";
import { OBase } from "./o-base";
import { ODer } from "./o-der";
import { Page } from "./page";
import { isPoint, type Point } from "./point";
import { isPoint3, type Point3 } from "./point3";
import { isRect, type Rect } from "./rect";
import { SBase } from "./s-base";
import { SPoint } from "./s-point";
import { Schedule } from "./schedule";
import type { Shape } from "./shape";
import { SilenceClip } from "./silence-clip";
import { Sq } from "./sq";
import { SqB } from "./sq-b";
import { Timeline } from "./timeline";
import { Title } from "./title";
import { UBaseA } from "./u-base-a";
import { UBaseH } from "./u-base-h";
import { UFail } from "./u-fail";
import { UFailY } from "./u-fail-y";
import { UNear } from "./u-near";
import { UNearA } from "./u-near-a";
import { UNearAbs } from "./u-near-abs";

export class StudioJsonContext extends SerializerContext {
  private static readonly _default: StudioJsonContext = new StudioJsonContext();

  private _clip?: TypeSpec<Clip>;

  private _timeline?: TypeSpec<Timeline>;

  private _listClip?: ValueSpec<Clip[] | null>;

  private _dictionaryStringClip?: ValueSpec<Map<string, Clip> | null>;

  private _iEnumerableClip?: ValueSpec<Iterable<Clip> | null>;

  private _group?: TypeSpec<Group>;

  private _hBase?: TypeSpec<HBase>;

  private _iBase?: TypeSpec<IBase>;

  private _dBase?: TypeSpec<DBase>;

  private _sBase?: TypeSpec<SBase>;

  private _uFail?: TypeSpec<UFail>;

  private _uNear?: TypeSpec<UNear>;

  private _uBaseH?: TypeSpec<UBaseH>;

  private _nBase?: TypeSpec<NBase>;

  private _nMid?: TypeSpec<NMid>;

  private _oBase?: TypeSpec<OBase>;

  private _gBaseInt32?: TypeSpec<GBase<number>>;

  private _feed?: TypeSpec<Feed>;

  private _iShape?: TypeSpec<IShape>;

  private _iShapeB?: TypeSpec<IShapeB>;

  private _shape?: TypeSpec<Shape>;

  private _mixedBase?: TypeSpec<MixedBase>;

  private _episodeSummary?: TypeSpec<EpisodeSummary>;

  private _iReadOnlyListEpisodeSummary?: ValueSpec<EpisodeSummary[] | null>;

  private _episodeSummaryArray?: ValueSpec<EpisodeSummary[] | null>;

  private _dictionaryStringEpisodeSummary?: ValueSpec<Map<string, EpisodeSummary> | null>;

  private _hashSetString?: ValueSpec<HashSet<string> | null>;

  private _intMapHolder?: TypeSpec<IntMapHolder>;

  private _levelMapHolder?: TypeSpec<LevelMapHolder>;

  private _guidMapHolder?: TypeSpec<GuidMapHolder>;

  private _listListInt32?: ValueSpec<number[][] | null>;

  private _string?: ValueSpec<string | null>;

  private _int32?: ValueSpec<number>;

  private _nullableInt32?: ValueSpec<number | null>;

  private _double?: ValueSpec<number>;

  private _boolean?: ValueSpec<boolean>;

  private _guid?: ValueSpec<UUID>;

  private _decimal?: ValueSpec<Decimal>;

  private _dateTime?: ValueSpec<Temporal.PlainDateTime>;

  private _dateOnly?: ValueSpec<Temporal.PlainDate>;

  private _timeOnly?: ValueSpec<Temporal.PlainTime>;

  private _dateTimeOffset?: ValueSpec<Temporal.ZonedDateTime>;

  private _timeSpan?: ValueSpec<Temporal.Duration>;

  private _listTimeOnly?: ValueSpec<Temporal.PlainTime[] | null>;

  private _dictionaryStringTimeOnly?: ValueSpec<Map<string, Temporal.PlainTime> | null>;

  private _schedule?: TypeSpec<Schedule>;

  private _holderInt?: TypeSpec<HolderInt>;

  private _holderLevel?: TypeSpec<HolderLevel>;

  private _holderChar?: TypeSpec<HolderChar>;

  private _holderSPoint?: TypeSpec<HolderSPoint>;

  private _listInt32?: ValueSpec<number[] | null>;

  private _listNullableInt32?: ValueSpec<(number | null)[] | null>;

  private _listString?: ValueSpec<string[] | null>;

  private _listLevel?: ValueSpec<Level[] | null>;

  private _listGuid?: ValueSpec<UUID[] | null>;

  private _listDateTime?: ValueSpec<Temporal.PlainDateTime[] | null>;

  private _hashSetInt32?: ValueSpec<HashSet<number> | null>;

  private _dictionaryStringInt32?: ValueSpec<Map<string, number> | null>;

  private _dictionaryStringNullableInt32?: ValueSpec<Map<string, number | null> | null>;

  private _int32Array?: ValueSpec<number[] | null>;

  private _color?: ValueSpec<Color>;

  private _level?: ValueSpec<Level>;

  private _flavor?: ValueSpec<Flavor>;

  private _sPoint?: TypeSpec<SPoint>;

  private _title?: ValueSpec<Title>;

  private _episodeRec?: TypeSpec<EpisodeRec>;

  private _imageClip?: TypeSpec<ImageClip>;

  private _silenceClip?: TypeSpec<SilenceClip>;

  private _hMid?: TypeSpec<HMid>;

  private _hLeaf?: TypeSpec<HLeaf>;

  private _iA?: TypeSpec<IA>;

  private _iS?: TypeSpec<IS>;

  private _dBer?: TypeSpec<DBer>;

  private _uFailY?: TypeSpec<UFailY>;

  private _uNearA?: TypeSpec<UNearA>;

  private _uNearAbs?: TypeSpec<UNearAbs>;

  private _uBaseA?: TypeSpec<UBaseA>;

  private _nLeaf?: TypeSpec<NLeaf>;

  private _oDer?: TypeSpec<ODer>;

  private _gDer?: TypeSpec<GDer>;

  private _pageEpisodeSummary?: TypeSpec<Page<EpisodeSummary>>;

  private _sq?: TypeSpec<Sq>;

  private _sqB?: TypeSpec<SqB>;

  private _point?: TypeSpec<Point>;

  private _point3?: TypeSpec<Point3>;

  private _rect?: TypeSpec<Rect>;

  private _mixedClass?: TypeSpec<MixedClass>;

  private _mixedPoint?: TypeSpec<MixedPoint>;

  static get default(): StudioJsonContext {
    return StudioJsonContext._default;
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

  get timeline(): TypeSpec<Timeline> {
    return (this._timeline ??= this.createSpec({
      type: Timeline,
      factory: (p: Record<string, unknown>) =>
        new Timeline(p.name as string, p.main as Clip, p.clips as Clip[]),
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
    }));
  }

  get listClip(): ValueSpec<Clip[] | null> {
    return (this._listClip ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.clip,
        },
      },
    }));
  }

  get dictionaryStringClip(): ValueSpec<Map<string, Clip> | null> {
    return (this._dictionaryStringClip ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: {
          kind: "ref",
          spec: () => this.clip,
        },
      },
    }));
  }

  get iEnumerableClip(): ValueSpec<Iterable<Clip> | null> {
    return (this._iEnumerableClip ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.clip,
        },
      },
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

  get hBase(): TypeSpec<HBase> {
    return (this._hBase ??= this.createSpec({
      type: HBase,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'HBase' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "a",
          json: "A",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "t",
        abstract: true,
        derived: [
          {
            tag: "mid",
            spec: () => this.hMid,
          },
          {
            tag: "leaf",
            spec: () => this.hLeaf,
          },
        ],
      },
    }));
  }

  get iBase(): TypeSpec<IBase> {
    return (this._iBase ??= this.createSpec({
      type: IBase,
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

  get dBase(): TypeSpec<DBase> {
    return (this._dBase ??= this.createSpec({
      type: DBase,
      factory: () => new DBase(),
      properties: [],
      polymorphism: {
        discriminator: "$type",
        derived: [
          {
            tag: "d",
            spec: () => this.dBer,
          },
        ],
      },
    }));
  }

  get sBase(): TypeSpec<SBase> {
    return (this._sBase ??= this.createSpec({
      type: SBase,
      factory: (p: Record<string, unknown>) => new SBase(p.v as number),
      properties: [
        {
          ts: "v",
          json: "V",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        derived: [
          {
            tag: "b",
            spec: () => this.sBase,
          },
        ],
      },
    }));
  }

  get uFail(): TypeSpec<UFail> {
    return (this._uFail ??= this.createSpec({
      type: UFail,
      factory: () => new UFail(),
      properties: [],
      polymorphism: {
        discriminator: "$type",
        derived: [
          {
            tag: "y",
            spec: () => this.uFailY,
          },
        ],
      },
    }));
  }

  get uNear(): TypeSpec<UNear> {
    return (this._uNear ??= this.createSpec({
      type: UNear,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'UNear' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "a",
          json: "A",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "a",
            spec: () => this.uNearA,
          },
          {
            tag: "abs",
            spec: () => this.uNearAbs,
            abstract: true,
          },
        ],
        unknownDerivedType: "fallBackToNearestAncestor",
      },
    }));
  }

  get uBaseH(): TypeSpec<UBaseH> {
    return (this._uBaseH ??= this.createSpec({
      type: UBaseH,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'UBaseH' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "a",
          json: "A",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "a",
            spec: () => this.uBaseA,
          },
        ],
        unknownDerivedType: "fallBackToBaseType",
      },
    }));
  }

  get nBase(): TypeSpec<NBase> {
    return (this._nBase ??= this.createSpec({
      type: NBase,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'NBase' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "a",
          json: "A",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "t",
        abstract: true,
        derived: [
          {
            tag: "mid",
            spec: () => this.nMid,
          },
        ],
      },
    }));
  }

  get nMid(): TypeSpec<NMid> {
    return (this._nMid ??= this.createSpec({
      type: NMid,
      base: this.nBase,
      factory: (p: Record<string, unknown>) => new NMid(p.a as number, p.b as number),
      properties: [
        {
          ts: "b",
          json: "B",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "u",
        derived: [
          {
            tag: "leaf",
            spec: () => this.nLeaf,
          },
        ],
      },
    }));
  }

  get oBase(): TypeSpec<OBase> {
    return (this._oBase ??= this.createSpec({
      type: OBase,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'OBase' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "base",
          json: "Base",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "o",
            spec: () => this.oDer,
          },
        ],
      },
    }));
  }

  get gBaseInt32(): TypeSpec<GBase<number>> {
    return (this._gBaseInt32 ??= this.createSpec<GBase<number>>({
      type: GBase,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'GBase' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "value",
          json: "Value",
          type: { kind: "primitive", numberType: "System.Int32" },
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
    }));
  }

  get feed(): TypeSpec<Feed> {
    return (this._feed ??= this.createSpec({
      type: Feed,
      factory: (p: Record<string, unknown>) => new Feed(p.latest as Page<EpisodeSummary>),
      properties: [
        {
          ts: "latest",
          json: "Latest",
          type: {
            kind: "ref",
            spec: () => this.pageEpisodeSummary,
          },
        },
      ],
    }));
  }

  get iShape(): TypeSpec<IShape> {
    return (this._iShape ??= this.createSpec({
      factory: () => {
        throw new Error(
          "Cannot deserialize interface type 'IShape' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        typeName: "IShape",
        derived: [
          {
            tag: "sq",
            spec: () => this.sq,
          },
        ],
      },
    }));
  }

  get iShapeB(): TypeSpec<IShapeB> {
    return (this._iShapeB ??= this.createSpec({
      factory: () => {
        throw new Error(
          "Cannot deserialize interface type 'IShapeB' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "area",
          json: "Area",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        typeName: "IShapeB",
        derived: [
          {
            tag: "sqb",
            spec: () => this.sqB,
          },
        ],
        unknownDerivedType: "fallBackToBaseType",
      },
    }));
  }

  get shape(): TypeSpec<Shape> {
    return (this._shape ??= this.createSpec({
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'Shape' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        typeName: "Shape",
        derived: [
          {
            tag: "point",
            spec: () => this.point,
            test: isPoint,
          },
          {
            tag: "point3",
            spec: () => this.point3,
            test: isPoint3,
          },
          {
            tag: "rect",
            spec: () => this.rect,
            test: isRect,
          },
        ],
        testOrder: [1, 0, 2],
      },
    }));
  }

  get mixedBase(): TypeSpec<MixedBase> {
    return (this._mixedBase ??= this.createSpec({
      type: MixedBase,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'MixedBase' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "cls",
            spec: () => this.mixedClass,
          },
          {
            tag: "pt",
            spec: () => this.mixedPoint,
            test: isMixedPoint,
          },
        ],
      },
    }));
  }

  get episodeSummary(): TypeSpec<EpisodeSummary> {
    return (this._episodeSummary ??= this.createSpec({
      type: EpisodeSummary,
      factory: (p: Record<string, unknown>) =>
        new EpisodeSummary(p.id as string, p.title as string),
      properties: [
        {
          ts: "id",
          json: "Id",
          type: { kind: "primitive" },
        },
        {
          ts: "title",
          json: "Title",
          type: { kind: "primitive" },
        },
      ],
    }));
  }

  get iReadOnlyListEpisodeSummary(): ValueSpec<EpisodeSummary[] | null> {
    return (this._iReadOnlyListEpisodeSummary ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.episodeSummary,
        },
      },
    }));
  }

  get episodeSummaryArray(): ValueSpec<EpisodeSummary[] | null> {
    return (this._episodeSummaryArray ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.episodeSummary,
        },
      },
    }));
  }

  get dictionaryStringEpisodeSummary(): ValueSpec<Map<string, EpisodeSummary> | null> {
    return (this._dictionaryStringEpisodeSummary ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: {
          kind: "ref",
          spec: () => this.episodeSummary,
        },
      },
    }));
  }

  get hashSetString(): ValueSpec<HashSet<string> | null> {
    return (this._hashSetString ??= this.createValueSpec({
      descriptor: {
        kind: "hashSet",
        element: { kind: "primitive" },
      },
    }));
  }

  get intMapHolder(): TypeSpec<IntMapHolder> {
    return (this._intMapHolder ??= this.createSpec({
      type: IntMapHolder,
      factory: (p: Record<string, unknown>) => new IntMapHolder(p.byKey as Map<number, string>),
      properties: [
        {
          ts: "byKey",
          json: "ByKey",
          type: {
            kind: "map",
            key: { kind: "primitive", numberType: "System.Int32" },
            value: { kind: "primitive" },
          },
        },
      ],
    }));
  }

  get levelMapHolder(): TypeSpec<LevelMapHolder> {
    return (this._levelMapHolder ??= this.createSpec({
      type: LevelMapHolder,
      factory: (p: Record<string, unknown>) => new LevelMapHolder(p.byLevel as Map<Level, string>),
      properties: [
        {
          ts: "byLevel",
          json: "ByLevel",
          type: {
            kind: "map",
            key: {
              kind: "numericEnum",
              values: Level,
              typeName: "SampleJsonContracts.Level",
              numberType: "System.Int32",
            },
            value: { kind: "primitive" },
          },
        },
      ],
    }));
  }

  get guidMapHolder(): TypeSpec<GuidMapHolder> {
    return (this._guidMapHolder ??= this.createSpec({
      type: GuidMapHolder,
      factory: (p: Record<string, unknown>) => new GuidMapHolder(p.byId as Map<UUID, string>),
      properties: [
        {
          ts: "byId",
          json: "ById",
          type: {
            kind: "map",
            key: { kind: "branded", create: UUID.create, typeName: "System.Guid" },
            value: { kind: "primitive" },
          },
        },
      ],
    }));
  }

  get listListInt32(): ValueSpec<number[][] | null> {
    return (this._listListInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "array",
          element: { kind: "primitive", numberType: "System.Int32" },
        },
      },
    }));
  }

  get string(): ValueSpec<string | null> {
    return (this._string ??= this.createValueSpec({
      descriptor: { kind: "primitive" },
    }));
  }

  get int32(): ValueSpec<number> {
    return (this._int32 ??= this.createValueSpec({
      descriptor: { kind: "primitive", numberType: "System.Int32" },
      valueType: true,
      typeName: "System.Int32",
    }));
  }

  get nullableInt32(): ValueSpec<number | null> {
    return (this._nullableInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "primitive", numberType: "System.Int32" },
      },
    }));
  }

  get double(): ValueSpec<number> {
    return (this._double ??= this.createValueSpec({
      descriptor: { kind: "primitive", numberType: "System.Double" },
      valueType: true,
      typeName: "System.Double",
    }));
  }

  get boolean(): ValueSpec<boolean> {
    return (this._boolean ??= this.createValueSpec({
      descriptor: { kind: "primitive", typeName: "System.Boolean" },
      valueType: true,
      typeName: "System.Boolean",
    }));
  }

  get guid(): ValueSpec<UUID> {
    return (this._guid ??= this.createValueSpec({
      descriptor: { kind: "branded", create: UUID.create, typeName: "System.Guid" },
      valueType: true,
      typeName: "System.Guid",
    }));
  }

  get decimal(): ValueSpec<Decimal> {
    return (this._decimal ??= this.createValueSpec({
      descriptor: { kind: "decimal", type: Decimal, typeName: "System.Decimal" },
      valueType: true,
      typeName: "System.Decimal",
    }));
  }

  get dateTime(): ValueSpec<Temporal.PlainDateTime> {
    return (this._dateTime ??= this.createValueSpec({
      descriptor: {
        kind: "temporal",
        parse: Temporal.PlainDateTime.from,
        typeName: "System.DateTime",
      },
      valueType: true,
      typeName: "System.DateTime",
    }));
  }

  get dateOnly(): ValueSpec<Temporal.PlainDate> {
    return (this._dateOnly ??= this.createValueSpec({
      descriptor: { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      valueType: true,
      typeName: "System.DateOnly",
    }));
  }

  get timeOnly(): ValueSpec<Temporal.PlainTime> {
    return (this._timeOnly ??= this.createValueSpec({
      descriptor: { kind: "temporal", parse: Temporal.PlainTime.from, typeName: "System.TimeOnly" },
      valueType: true,
      typeName: "System.TimeOnly",
    }));
  }

  get dateTimeOffset(): ValueSpec<Temporal.ZonedDateTime> {
    return (this._dateTimeOffset ??= this.createValueSpec({
      descriptor: {
        kind: "temporal",
        parse: Temporal.ZonedDateTime.from,
        typeName: "System.DateTimeOffset",
      },
      valueType: true,
      typeName: "System.DateTimeOffset",
    }));
  }

  get timeSpan(): ValueSpec<Temporal.Duration> {
    return (this._timeSpan ??= this.createValueSpec({
      descriptor: { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      valueType: true,
      typeName: "System.TimeSpan",
    }));
  }

  get listTimeOnly(): ValueSpec<Temporal.PlainTime[] | null> {
    return (this._listTimeOnly ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "temporal", parse: Temporal.PlainTime.from, typeName: "System.TimeOnly" },
      },
    }));
  }

  get dictionaryStringTimeOnly(): ValueSpec<Map<string, Temporal.PlainTime> | null> {
    return (this._dictionaryStringTimeOnly ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: { kind: "temporal", parse: Temporal.PlainTime.from, typeName: "System.TimeOnly" },
      },
    }));
  }

  get schedule(): TypeSpec<Schedule> {
    return (this._schedule ??= this.createSpec({
      type: Schedule,
      factory: (p: Record<string, unknown>) =>
        new Schedule(
          p.at as Temporal.PlainDateTime,
          p.starts as Temporal.PlainTime,
          p.day as Temporal.PlainDate,
          p.duration as Temporal.Duration,
          p.moment as Temporal.ZonedDateTime,
        ),
      properties: [
        {
          ts: "at",
          json: "At",
          type: {
            kind: "temporal",
            parse: Temporal.PlainDateTime.from,
            typeName: "System.DateTime",
          },
        },
        {
          ts: "starts",
          json: "Starts",
          type: { kind: "temporal", parse: Temporal.PlainTime.from, typeName: "System.TimeOnly" },
        },
        {
          ts: "day",
          json: "Day",
          type: { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
        },
        {
          ts: "duration",
          json: "Duration",
          type: { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
        },
        {
          ts: "moment",
          json: "Moment",
          type: {
            kind: "temporal",
            parse: Temporal.ZonedDateTime.from,
            typeName: "System.DateTimeOffset",
          },
        },
      ],
    }));
  }

  get holderInt(): TypeSpec<HolderInt> {
    return (this._holderInt ??= this.createSpec({
      type: HolderInt,
      factory: (p: Record<string, unknown>) => new HolderInt(p.n as number),
      properties: [
        {
          ts: "n",
          json: "N",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get holderLevel(): TypeSpec<HolderLevel> {
    return (this._holderLevel ??= this.createSpec({
      type: HolderLevel,
      factory: (p: Record<string, unknown>) => new HolderLevel(p.level as Level),
      properties: [
        {
          ts: "level",
          json: "Level",
          type: { kind: "numericEnum", values: Level, typeName: "SampleJsonContracts.Level" },
        },
      ],
    }));
  }

  get holderChar(): TypeSpec<HolderChar> {
    return (this._holderChar ??= this.createSpec({
      type: HolderChar,
      factory: (p: Record<string, unknown>) => new HolderChar(p.c as string),
      properties: [
        {
          ts: "c",
          json: "C",
          type: { kind: "primitive", typeName: "System.Char" },
        },
      ],
    }));
  }

  get holderSPoint(): TypeSpec<HolderSPoint> {
    return (this._holderSPoint ??= this.createSpec({
      type: HolderSPoint,
      factory: (p: Record<string, unknown>) => new HolderSPoint(p.s as SPoint),
      properties: [
        {
          ts: "s",
          json: "S",
          type: {
            kind: "ref",
            spec: () => this.sPoint,
          },
        },
      ],
    }));
  }

  get listInt32(): ValueSpec<number[] | null> {
    return (this._listInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "primitive", numberType: "System.Int32" },
      },
    }));
  }

  get listNullableInt32(): ValueSpec<(number | null)[] | null> {
    return (this._listNullableInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "nullable",
          inner: { kind: "primitive", numberType: "System.Int32" },
        },
      },
    }));
  }

  get listString(): ValueSpec<string[] | null> {
    return (this._listString ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "primitive" },
      },
    }));
  }

  get listLevel(): ValueSpec<Level[] | null> {
    return (this._listLevel ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "numericEnum", values: Level, typeName: "SampleJsonContracts.Level" },
      },
    }));
  }

  get listGuid(): ValueSpec<UUID[] | null> {
    return (this._listGuid ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "branded", create: UUID.create, typeName: "System.Guid" },
      },
    }));
  }

  get listDateTime(): ValueSpec<Temporal.PlainDateTime[] | null> {
    return (this._listDateTime ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "temporal",
          parse: Temporal.PlainDateTime.from,
          typeName: "System.DateTime",
        },
      },
    }));
  }

  get hashSetInt32(): ValueSpec<HashSet<number> | null> {
    return (this._hashSetInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "hashSet",
        element: { kind: "primitive", numberType: "System.Int32" },
      },
    }));
  }

  get dictionaryStringInt32(): ValueSpec<Map<string, number> | null> {
    return (this._dictionaryStringInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: { kind: "primitive", numberType: "System.Int32" },
      },
    }));
  }

  get dictionaryStringNullableInt32(): ValueSpec<Map<string, number | null> | null> {
    return (this._dictionaryStringNullableInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: {
          kind: "nullable",
          inner: { kind: "primitive", numberType: "System.Int32" },
        },
      },
    }));
  }

  get int32Array(): ValueSpec<number[] | null> {
    return (this._int32Array ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "primitive", numberType: "System.Int32" },
      },
    }));
  }

  get color(): ValueSpec<Color> {
    return (this._color ??= this.createValueSpec({
      descriptor: { kind: "numericEnum", values: Color, typeName: "SampleJsonContracts.Color" },
      valueType: true,
      typeName: "SampleJsonContracts.Color",
    }));
  }

  get level(): ValueSpec<Level> {
    return (this._level ??= this.createValueSpec({
      descriptor: { kind: "numericEnum", values: Level, typeName: "SampleJsonContracts.Level" },
      valueType: true,
      typeName: "SampleJsonContracts.Level",
    }));
  }

  get flavor(): ValueSpec<Flavor> {
    return (this._flavor ??= this.createValueSpec({
      descriptor: { kind: "enum", values: Flavor, typeName: "SampleJsonContracts.Flavor" },
      valueType: true,
      typeName: "SampleJsonContracts.Flavor",
    }));
  }

  get sPoint(): TypeSpec<SPoint> {
    return (this._sPoint ??= this.createSpec({
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
      typeName: "SampleJsonContracts.SPoint",
    }));
  }

  get title(): ValueSpec<Title> {
    return (this._title ??= this.createValueSpec({
      descriptor: { kind: "branded", create: Title.from, typeName: "SampleJsonContracts.Title" },
      valueType: true,
      typeName: "SampleJsonContracts.Title",
    }));
  }

  get episodeRec(): TypeSpec<EpisodeRec> {
    return (this._episodeRec ??= this.createSpec({
      type: EpisodeRec,
      factory: (p: Record<string, unknown>) => new EpisodeRec(p.id as string, p.note as Title),
      properties: [
        {
          ts: "id",
          json: "Id",
          type: { kind: "primitive" },
        },
        {
          ts: "note",
          json: "Note",
          type: { kind: "branded", create: Title.from, typeName: "SampleJsonContracts.Title" },
        },
      ],
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

  get hMid(): TypeSpec<HMid> {
    return (this._hMid ??= this.createSpec({
      type: HMid,
      base: this.hBase,
      factory: (p: Record<string, unknown>) => new HMid(p.a as number, p.b as number),
      properties: [
        {
          ts: "b",
          json: "B",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get hLeaf(): TypeSpec<HLeaf> {
    return (this._hLeaf ??= this.createSpec({
      type: HLeaf,
      base: this.hMid,
      factory: (p: Record<string, unknown>) =>
        new HLeaf(p.a as number, p.b as number, p.c as number),
      properties: [
        {
          ts: "c",
          json: "C",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get iA(): TypeSpec<IA> {
    return (this._iA ??= this.createSpec({
      type: IA,
      base: this.iBase,
      factory: (p: Record<string, unknown>) => new IA(p.x as number),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get iS(): TypeSpec<IS> {
    return (this._iS ??= this.createSpec({
      type: IS,
      base: this.iBase,
      factory: (p: Record<string, unknown>) => new IS(p.y as number),
      properties: [
        {
          ts: "y",
          json: "Y",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get dBer(): TypeSpec<DBer> {
    return (this._dBer ??= this.createSpec({
      type: DBer,
      base: this.dBase,
      factory: (p: Record<string, unknown>) => new DBer(p.w as number),
      properties: [
        {
          ts: "w",
          json: "W",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get uFailY(): TypeSpec<UFailY> {
    return (this._uFailY ??= this.createSpec({
      type: UFailY,
      base: this.uFail,
      factory: () => new UFailY(),
      properties: [],
    }));
  }

  get uNearA(): TypeSpec<UNearA> {
    return (this._uNearA ??= this.createSpec({
      type: UNearA,
      base: this.uNear,
      factory: (p: Record<string, unknown>) => new UNearA(p.a as number, p.b as number),
      properties: [
        {
          ts: "b",
          json: "B",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get uNearAbs(): TypeSpec<UNearAbs> {
    return (this._uNearAbs ??= this.createSpec({
      type: UNearAbs,
      base: this.uNear,
      factory: () => {
        throw new Error("Cannot construct abstract type 'UNearAbs'.");
      },
      properties: [],
    }));
  }

  get uBaseA(): TypeSpec<UBaseA> {
    return (this._uBaseA ??= this.createSpec({
      type: UBaseA,
      base: this.uBaseH,
      factory: (p: Record<string, unknown>) => new UBaseA(p.a as number),
      properties: [],
    }));
  }

  get nLeaf(): TypeSpec<NLeaf> {
    return (this._nLeaf ??= this.createSpec({
      type: NLeaf,
      base: this.nMid,
      factory: (p: Record<string, unknown>) =>
        new NLeaf(p.a as number, p.b as number, p.c as number),
      properties: [
        {
          ts: "c",
          json: "C",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get oDer(): TypeSpec<ODer> {
    return (this._oDer ??= this.createSpec({
      type: ODer,
      base: this.oBase,
      factory: (p: Record<string, unknown>) =>
        Object.assign(new ODer(), {
          base: p.base as number,
          name: p.name as string,
          x: p.x as number,
        }),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
      ],
    }));
  }

  get gDer(): TypeSpec<GDer> {
    return (this._gDer ??= this.createSpec({
      type: GDer,
      base: this.gBaseInt32,
      factory: (p: Record<string, unknown>) =>
        Object.assign(new GDer(p.extra as number), { value: p.value as number }),
      properties: [
        {
          ts: "extra",
          json: "Extra",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get pageEpisodeSummary(): TypeSpec<Page<EpisodeSummary>> {
    return (this._pageEpisodeSummary ??= this.createSpec<Page<EpisodeSummary>>({
      type: Page,
      factory: (p: Record<string, unknown>) => new Page(p.items as EpisodeSummary[]),
      properties: [
        {
          ts: "items",
          json: "Items",
          type: {
            kind: "array",
            element: {
              kind: "ref",
              spec: () => this.episodeSummary,
            },
          },
        },
      ],
    }));
  }

  get sq(): TypeSpec<Sq> {
    return (this._sq ??= this.createSpec({
      type: Sq,
      factory: (p: Record<string, unknown>) => new Sq(p.side as number),
      properties: [
        {
          ts: "side",
          json: "Side",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
    }));
  }

  get sqB(): TypeSpec<SqB> {
    return (this._sqB ??= this.createSpec({
      type: SqB,
      factory: (p: Record<string, unknown>) => new SqB(p.area as number, p.side as number),
      properties: [
        {
          ts: "area",
          json: "Area",
          type: { kind: "primitive", numberType: "System.Double" },
        },
        {
          ts: "side",
          json: "Side",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
    }));
  }

  get point(): TypeSpec<Point> {
    return (this._point ??= this.createSpec({
      base: this.shape,
      factory: (p: Record<string, unknown>) => ({ x: p.x as number }),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
    }));
  }

  get point3(): TypeSpec<Point3> {
    return (this._point3 ??= this.createSpec({
      base: this.point,
      factory: (p: Record<string, unknown>) => ({
        x: p.x as number,
        y: p.y as number,
        z: p.z as number,
      }),
      properties: [
        {
          ts: "y",
          json: "Y",
          type: { kind: "primitive", numberType: "System.Double" },
        },
        {
          ts: "z",
          json: "Z",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
    }));
  }

  get rect(): TypeSpec<Rect> {
    return (this._rect ??= this.createSpec({
      base: this.shape,
      factory: (p: Record<string, unknown>) => ({ w: p.w as number, h: p.h as number }),
      properties: [
        {
          ts: "w",
          json: "W",
          type: { kind: "primitive", numberType: "System.Double" },
        },
        {
          ts: "h",
          json: "H",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
    }));
  }

  get mixedClass(): TypeSpec<MixedClass> {
    return (this._mixedClass ??= this.createSpec({
      type: MixedClass,
      base: this.mixedBase,
      factory: (p: Record<string, unknown>) => new MixedClass(p.w as number),
      properties: [
        {
          ts: "w",
          json: "W",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get mixedPoint(): TypeSpec<MixedPoint> {
    return (this._mixedPoint ??= this.createSpec({
      base: this.mixedBase,
      factory: (p: Record<string, unknown>) => ({ x: p.x as number }),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }
}
