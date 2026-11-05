import { Temporal } from "@js-temporal/polyfill";
import { HashSet, UUID } from "metano-runtime";
import { Decimal } from "decimal.js";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { EpisodeId } from "./episode-id";
import { EpisodeSummary } from "./episode-summary";
import { Leaf } from "./leaf";
import { Level } from "./level";
import { Mood } from "./mood";
import { ShotTiming } from "./shot-timing";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _episodeSummary?: TypeSpec<EpisodeSummary>;

  private _iReadOnlyListEpisodeSummary?: ValueSpec<EpisodeSummary[] | null>;

  private _episodeSummaryArray?: ValueSpec<EpisodeSummary[] | null>;

  private _shotTimingArray?: ValueSpec<ShotTiming[] | null>;

  private _dictionaryStringEpisodeSummary?: ValueSpec<Map<string, EpisodeSummary> | null>;

  private _hashSetString?: ValueSpec<HashSet<string> | null>;

  private _iEnumerableLeaf?: ValueSpec<Iterable<Leaf> | null>;

  private _nullableInt32?: ValueSpec<number | null>;

  private _int32?: ValueSpec<number>;

  private _string?: ValueSpec<string | null>;

  private _guid?: ValueSpec<UUID>;

  private _dateTimeOffset?: ValueSpec<Temporal.ZonedDateTime>;

  private _decimal?: ValueSpec<Decimal>;

  private _mood?: ValueSpec<Mood>;

  private _level?: ValueSpec<Level>;

  private _episodeId?: ValueSpec<EpisodeId>;

  private _names?: ValueSpec<string[] | null>;

  private _shotTiming?: TypeSpec<ShotTiming>;

  private _leaf?: TypeSpec<Leaf>;

  static get default(): JsonContext {
    return this._default;
  }

  get episodeSummary(): TypeSpec<EpisodeSummary> {
    return this._episodeSummary ??= this.createSpec({
      type: EpisodeSummary,
      factory: (p: Record<string, unknown>) => new EpisodeSummary(p.title as string, p.number as number),
      properties: [
        {
          ts: "title",
          json: "title",
          type: { kind: "primitive" },
        },
        {
          ts: "number",
          json: "number",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get iReadOnlyListEpisodeSummary(): ValueSpec<EpisodeSummary[] | null> {
    return this._iReadOnlyListEpisodeSummary ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.episodeSummary,
        },
      },
    });
  }

  get episodeSummaryArray(): ValueSpec<EpisodeSummary[] | null> {
    return this._episodeSummaryArray ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.episodeSummary,
        },
      },
    });
  }

  get shotTimingArray(): ValueSpec<ShotTiming[] | null> {
    return this._shotTimingArray ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.shotTiming,
        },
      },
    });
  }

  get dictionaryStringEpisodeSummary(): ValueSpec<Map<string, EpisodeSummary> | null> {
    return this._dictionaryStringEpisodeSummary ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: {
          kind: "ref",
          spec: () => this.episodeSummary,
        },
      },
    });
  }

  get hashSetString(): ValueSpec<HashSet<string> | null> {
    return this._hashSetString ??= this.createValueSpec({
      descriptor: {
        kind: "hashSet",
        element: { kind: "primitive" },
      },
    });
  }

  get iEnumerableLeaf(): ValueSpec<Iterable<Leaf> | null> {
    return this._iEnumerableLeaf ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: {
          kind: "ref",
          spec: () => this.leaf,
        },
      },
    });
  }

  get nullableInt32(): ValueSpec<number | null> {
    return this._nullableInt32 ??= this.createValueSpec({
      descriptor: {
        kind: "nullable",
        inner: { kind: "primitive" },
      },
    });
  }

  get int32(): ValueSpec<number> {
    return this._int32 ??= this.createValueSpec({
      descriptor: { kind: "primitive" },
      valueType: true,
      typeName: "System.Int32",
    });
  }

  get string(): ValueSpec<string | null> {
    return this._string ??= this.createValueSpec({
      descriptor: { kind: "primitive" },
    });
  }

  get guid(): ValueSpec<UUID> {
    return this._guid ??= this.createValueSpec({
      descriptor: { kind: "branded", create: UUID.create },
      valueType: true,
      typeName: "System.Guid",
    });
  }

  get dateTimeOffset(): ValueSpec<Temporal.ZonedDateTime> {
    return this._dateTimeOffset ??= this.createValueSpec({
      descriptor: { kind: "temporal", parse: Temporal.ZonedDateTime.from, typeName: "System.DateTimeOffset" },
      valueType: true,
      typeName: "System.DateTimeOffset",
    });
  }

  get decimal(): ValueSpec<Decimal> {
    return this._decimal ??= this.createValueSpec({
      descriptor: { kind: "decimal", type: Decimal },
      valueType: true,
      typeName: "System.Decimal",
    });
  }

  get mood(): ValueSpec<Mood> {
    return this._mood ??= this.createValueSpec({
      descriptor: { kind: "enum", values: Mood },
      valueType: true,
      typeName: "TestApp.Mood",
    });
  }

  get level(): ValueSpec<Level> {
    return this._level ??= this.createValueSpec({
      descriptor: { kind: "numericEnum", values: Level },
      valueType: true,
      typeName: "TestApp.Level",
    });
  }

  get episodeId(): ValueSpec<EpisodeId> {
    return this._episodeId ??= this.createValueSpec({
      descriptor: { kind: "branded", create: EpisodeId.create },
      valueType: true,
      typeName: "TestApp.EpisodeId",
    });
  }

  get names(): ValueSpec<string[] | null> {
    return this._names ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "primitive" },
      },
    });
  }

  get shotTiming(): TypeSpec<ShotTiming> {
    return this._shotTiming ??= this.createSpec({
      type: ShotTiming,
      factory: (p: Record<string, unknown>) => new ShotTiming(p.start as number, p.end as number),
      properties: [
        {
          ts: "start",
          json: "start",
          type: { kind: "primitive" },
        },
        {
          ts: "end",
          json: "end",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get leaf(): TypeSpec<Leaf> {
    return this._leaf ??= this.createSpec({
      type: Leaf,
      factory: (p: Record<string, unknown>) => new Leaf(p.name as string),
      properties: [
        {
          ts: "name",
          json: "name",
          type: { kind: "primitive" },
        },
      ],
    });
  }
}
