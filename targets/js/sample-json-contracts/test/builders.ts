import { expect } from "bun:test";

import { Temporal } from "@js-temporal/polyfill";
import { Decimal } from "decimal.js";

import { UUID } from "metano-runtime";

import { Account } from "#/sample-json-contracts/account";
import { Blob } from "#/sample-json-contracts/blob";
import type { Clip } from "#/sample-json-contracts/clip";
import { Color } from "#/sample-json-contracts/color";
import { DBer } from "#/sample-json-contracts/d-ber";
import { DBase } from "#/sample-json-contracts/d-base";
import { EpisodeRec } from "#/sample-json-contracts/episode-rec";
import { EpisodeSummary } from "#/sample-json-contracts/episode-summary";
import { Feed } from "#/sample-json-contracts/feed";
import { Flavor } from "#/sample-json-contracts/flavor";
import { GDer } from "#/sample-json-contracts/g-der";
import { Group } from "#/sample-json-contracts/group";
import { GuidMapHolder } from "#/sample-json-contracts/guid-map-holder";
import { HLeaf } from "#/sample-json-contracts/h-leaf";
import { HMid } from "#/sample-json-contracts/h-mid";
import { IA } from "#/sample-json-contracts/ia";
import { IgnBase } from "#/sample-json-contracts/ign-base";
import { ImageClip } from "#/sample-json-contracts/image-clip";
import { InitRec } from "#/sample-json-contracts/init-rec";
import { Int64Box } from "#/sample-json-contracts/int64-box";
import { IntMapHolder } from "#/sample-json-contracts/int-map-holder";
import { IS } from "#/sample-json-contracts/is";
import { Level } from "#/sample-json-contracts/level";
import { LevelMapHolder } from "#/sample-json-contracts/level-map-holder";
import { MixedClass } from "#/sample-json-contracts/mixed-class";
import { NLeaf } from "#/sample-json-contracts/n-leaf";
import { NMid } from "#/sample-json-contracts/n-mid";
import { Nums } from "#/sample-json-contracts/nums";
import { ODer } from "#/sample-json-contracts/o-der";
import { OrdBase } from "#/sample-json-contracts/ord-base";
import { Other } from "#/sample-json-contracts/other";
import { OtherB } from "#/sample-json-contracts/other-b";
import { Page } from "#/sample-json-contracts/page";
import { Schedule } from "#/sample-json-contracts/schedule";
import { SPoint } from "#/sample-json-contracts/s-point";
import { SBase } from "#/sample-json-contracts/s-base";
import { SetClass } from "#/sample-json-contracts/set-class";
import { SilenceClip } from "#/sample-json-contracts/silence-clip";
import { SoftNums } from "#/sample-json-contracts/soft-nums";
import { Sq } from "#/sample-json-contracts/sq";
import { SqB } from "#/sample-json-contracts/sq-b";
import { Stats } from "#/sample-json-contracts/stats";
import { Timeline } from "#/sample-json-contracts/timeline";
import { Title } from "#/sample-json-contracts/title";
import { UBaseX } from "#/sample-json-contracts/u-base-x";
import { UFailX } from "#/sample-json-contracts/u-fail-x";
import { UNearA } from "#/sample-json-contracts/u-near-a";
import { UNearB } from "#/sample-json-contracts/u-near-b";

/** The `Guid` key of row V21. */
const V21_GUID = "0f8fad5b-d9cb-469f-a165-70867728950e";

/**
 * Per-id builders for the shared STJ conformance manifest
 * (`fixtures/stj/cases.json`, spec 009 contracts/wire-format.md).
 *
 * A `ts` row that needs a builder (every row but a read error) and has none
 * fails the conformance test, and an entry here without a manifest row fails
 * too, so the two sides cannot drift by omission.
 *
 * Entries follow the manifest order. A read error row needs no entry; one
 * exists only where a flag carries information (`viaText`).
 *
 * `build` constructs the value for write and roundtrip rows; `assertRead`
 * verifies the deserialized value of read and roundtrip rows. `plainObject`
 * marks rows whose TS read is a plain object (`[PlainObject]` contracts),
 * which have no constructor to check with `instanceof`. `viaText` marks rows
 * that must be read through `JsonSerializer.deserializeText` (the exact
 * decimal paths, FR-048 item 7).
 */
export interface CaseBuilder {
  readonly build?: () => unknown;
  readonly assertRead?: (value: unknown) => void;
  readonly plainObject?: boolean;
  readonly viaText?: boolean;
}

export const caseBuilders: Record<string, CaseBuilder> = {
  W1: {
    build: () => new ImageClip("a.png", 1.5),
    assertRead: (value) => {
      const clip = expectClass(value, ImageClip);

      expect(clip.path).toBe("a.png");
      expect(clip.seconds).toBe(1.5);
    },
  },
  W2: {
    build: () => new SilenceClip(2),
    assertRead: (value) => {
      const clip = expectClass(value, SilenceClip);

      expect(clip.seconds).toBe(2);
    },
  },
  W3: {
    build: () => new ImageClip("a.png", 1.5),
    assertRead: (value) => {
      const clip = expectClass(value, ImageClip);

      expect(clip.path).toBe("a.png");
    },
  },
  W4: {
    build: () => new Timeline("t", new ImageClip("a.png", 1.5), [new SilenceClip(2)]),
    assertRead: (value) => {
      const timeline = expectClass(value, Timeline);

      expect(timeline.name).toBe("t");
      expect(timeline.main).toBeInstanceOf(ImageClip);
      expect((timeline.main as ImageClip).path).toBe("a.png");
      expect(timeline.clips).toHaveLength(1);
      expect(timeline.clips[0]).toBeInstanceOf(SilenceClip);
      expect((timeline.clips[0] as SilenceClip).seconds).toBe(2);
    },
  },
  W5: {
    build: () => [new ImageClip("a", 1.5), new SilenceClip(2)],
    assertRead: (value) => {
      const clips = value as Clip[];

      expect(clips).toHaveLength(2);
      expect(clips[0]).toBeInstanceOf(ImageClip);
      expect((clips[0] as ImageClip).path).toBe("a");
      expect(clips[1]).toBeInstanceOf(SilenceClip);
      expect((clips[1] as SilenceClip).seconds).toBe(2);
    },
  },
  W6: {
    build: () => new Map([["x", new SilenceClip(2)]]),
    assertRead: (value) => {
      const clips = value as Map<string, Clip>;

      expect(clips.get("x")).toBeInstanceOf(SilenceClip);
      expect((clips.get("x") as SilenceClip).seconds).toBe(2);
    },
  },
  W7: {
    build: () => new HLeaf(1, 2, 3),
    assertRead: (value) => {
      const leaf = expectClass(value, HLeaf);

      expect(leaf.a).toBe(1);
      expect(leaf.b).toBe(2);
      expect(leaf.c).toBe(3);
    },
  },
  W8: { build: () => new HLeaf(1, 2, 3) },
  W9: {
    build: () => new IA(5),
    assertRead: (value) => {
      const ia = expectClass(value, IA);

      expect(ia.x).toBe(5);
    },
  },
  W10: {
    build: () => new IS(2),
    assertRead: (value) => {
      const is = expectClass(value, IS);

      expect(is.y).toBe(2);
    },
  },
  W11: {
    build: () => new DBase(),
    assertRead: (value) => expectClass(value, DBase),
  },
  W11b: {
    build: () => new SBase(1),
    assertRead: (value) => {
      const base = expectClass(value, SBase);

      expect(base.v).toBe(1);
    },
  },
  W11c: {
    build: () => new DBer(2),
    assertRead: (value) => {
      const ber = expectClass(value, DBer);

      expect(ber.w).toBe(2);
    },
  },
  W12: { build: () => new UNearB(1, 2, 3) },
  W13: { build: () => new UBaseX(1, 2) },
  W15: {
    build: () => [new EpisodeSummary("a", "b")],
    assertRead: (value) => {
      const summaries = value as EpisodeSummary[];

      expect(summaries).toHaveLength(1);
      expect(summaries[0]).toBeInstanceOf(EpisodeSummary);
      expect(summaries[0]?.id).toBe("a");
      expect(summaries[0]?.title).toBe("b");
    },
  },
  W17: {
    build: () => new NMid(1, 2),
    assertRead: (value) => {
      const mid = expectClass(value, NMid);

      expect(mid.a).toBe(1);
      expect(mid.b).toBe(2);
    },
  },
  W18: {
    build: () => new NLeaf(1, 2, 3),
    assertRead: (value) => {
      const leaf = expectClass(value, NLeaf);

      expect(leaf.a).toBe(1);
      expect(leaf.b).toBe(2);
      expect(leaf.c).toBe(3);
    },
  },
  W19: {
    build: () => Object.assign(new ODer(), { x: 2, name: "x", base: 1 }),
    assertRead: (value) => {
      const der = expectClass(value, ODer);

      expect(der.x).toBe(2);
      expect(der.name).toBe("x");
      expect(der.base).toBe(1);
    },
  },
  W20: {
    build: () => new GDer(2),
    assertRead: (value) => {
      const der = expectClass(value, GDer);

      expect(der.extra).toBe(2);
      expect(der.value).toBe(1);
    },
  },
  W21: {
    build: () => new Feed(new Page([new EpisodeSummary("a", "b")])),
    assertRead: (value) => {
      const feed = expectClass(value, Feed);

      expect(feed.latest.items).toHaveLength(1);
      expect(feed.latest.items[0]).toBeInstanceOf(EpisodeSummary);
      expect(feed.latest.items[0]?.id).toBe("a");
    },
  },
  W22: { build: () => null },
  W22b: { build: () => null },
  W23: {
    build: function* () {
      yield new SilenceClip(2);
    },
  },
  W24: {
    build: () => new Sq(2),
    assertRead: (value) => {
      const sq = expectClass(value, Sq);

      expect(sq.side).toBe(2);
    },
  },
  W25: { build: () => new OtherB(3) },
  W26: {
    build: () => ({ x: 1 }),
    assertRead: (value) => {
      expectPlainObject(value);

      expect((value as { x: number }).x).toBe(1);
    },
    plainObject: true,
  },
  W27: {
    build: () => ({ x: 1, y: 2, z: 3 }),
    assertRead: (value) => {
      expectPlainObject(value);

      const point = value as { x: number; y: number; z: number };

      expect(point.x).toBe(1);
      expect(point.y).toBe(2);
      expect(point.z).toBe(3);
    },
    plainObject: true,
  },
  W28a: {
    build: () => new MixedClass(2),
    assertRead: (value) => {
      const mixed = expectClass(value, MixedClass);

      expect(mixed.w).toBe(2);
    },
  },
  W28b: {
    build: () => ({ x: 5 }),
    assertRead: (value) => {
      expectPlainObject(value);

      expect((value as { x: number }).x).toBe(5);
    },
    plainObject: true,
  },
  W29: {
    build: () => ({ w: 1, h: 2 }),
    assertRead: (value) => {
      expectPlainObject(value);

      const rect = value as { w: number; h: number };

      expect(rect.w).toBe(1);
      expect(rect.h).toBe(2);
    },
    plainObject: true,
  },
  W30: {
    build: () => new SqB(4, 2),
    assertRead: (value) => {
      const square = expectClass(value, SqB);

      expect(square.area).toBe(4);
      expect(square.side).toBe(2);
    },
  },
  R1: {
    assertRead: (value) => {
      const clip = expectClass(value, ImageClip);

      expect(clip.path).toBe("a");
      expect(clip.seconds).toBe(1);
    },
  },
  R2: {
    assertRead: (value) => {
      const base = expectClass(value, IgnBase);

      expect(base.v).toBe(1);
    },
  },
  R3: {
    assertRead: (value) => {
      const clip = expectClass(value, ImageClip);

      expect(clip.path).toBe("a");
    },
  },
  R4: {
    assertRead: (value) => {
      const mid = expectClass(value, HMid);

      expect(mid.a).toBe(1);
      expect(mid.b).toBe(2);
    },
  },
  R5: {
    assertRead: (value) => {
      const mid = expectClass(value, NMid);

      expect(mid.a).toBe(1);
      expect(mid.b).toBe(2);
    },
  },
  R6: {
    assertRead: (value) => {
      expectPlainObject(value);

      expect((value as { x: number }).x).toBe(1);
    },
    plainObject: true,
  },
  R7: {
    assertRead: (value) => {
      const set = expectClass(value, SetClass);

      expect(set.n).toBe(1);
      expect(set.count).toBe(5);
    },
  },
  R8: {
    assertRead: (value) => {
      const near = expectClass(value, UNearA);

      expect(near.a).toBe(1);
      expect(near.b).toBe(2);
    },
  },
  V1: { build: () => "hello", assertRead: (value) => expect(value).toBe("hello") },
  V2: { build: () => 42, assertRead: (value) => expect(value).toBe(42) },
  V3: { build: () => 2.5, assertRead: (value) => expect(value).toBe(2.5) },
  V4: { build: () => 7, assertRead: (value) => expect(value).toBe(7) },
  V5: { build: () => true, assertRead: (value) => expect(value).toBe(true) },
  V6: {
    build: () => UUID.create("81473d0e-4b8d-4d63-b8c7-2f9e1a5d3c47"),
    assertRead: (value) => expect(value).toBe("81473d0e-4b8d-4d63-b8c7-2f9e1a5d3c47"),
  },
  V7a: {
    build: () => new Decimal("79228162514264337593543950335"),
    assertRead: (value) => expectDecimal(value, "79228162514264337593543950335"),
    viaText: true,
  },
  V7b: {
    build: () => new Decimal("0.0000001"),
    assertRead: (value) => expectDecimal(value, "0.0000001"),
    viaText: true,
  },
  V8: {
    build: () => Temporal.PlainDateTime.from("2026-01-02T03:04:05.678"),
    assertRead: (value) => expect(String(value)).toBe("2026-01-02T03:04:05.678"),
  },
  V9: {
    build: () => Temporal.PlainDate.from("2026-01-02"),
    assertRead: (value) => expect(String(value)).toBe("2026-01-02"),
  },
  V10: {
    build: () => Temporal.PlainTime.from("03:04:05.678"),
    assertRead: (value) => expect(String(value)).toBe("03:04:05.678"),
  },
  V11b: {
    build: () => Temporal.ZonedDateTime.from("2026-01-02T03:04:05+00:00[UTC]"),
  },
  V12b: {
    build: () => Temporal.Duration.from({ hours: 1, minutes: 2, seconds: 3 }),
  },
  V13: { build: () => 1, assertRead: (value) => expect(value).toBe(1) },
  V14: { build: () => Flavor.Sweet, assertRead: (value) => expect(value).toBe("Sweet") },
  V15: {
    build: () => new Set(["a", "b"]),
    assertRead: (value) => {
      const values = value as Set<string>;

      expect([...values]).toEqual(["a", "b"]);
    },
  },
  V16: {
    build: () => new Map([["x", new EpisodeSummary("a", "b")]]),
    assertRead: (value) => {
      const summaries = value as Map<string, EpisodeSummary>;

      expect(summaries.get("x")).toBeInstanceOf(EpisodeSummary);
      expect((summaries.get("x") as EpisodeSummary).id).toBe("a");
    },
  },
  V17: {
    build: () =>
      new IntMapHolder(
        new Map([
          [5, "e"],
          [1, "a"],
        ]),
      ),
    assertRead: (value) => {
      const holder = expectClass(value, IntMapHolder);

      expect(holder.byKey.get(5)).toBe("e");
      expect(holder.byKey.get(1)).toBe("a");
    },
  },
  V18: {
    build: () => new SPoint(1, 2),
    assertRead: (value) => {
      const point = expectClass(value, SPoint);

      expect(point.x).toBe(1);
      expect(point.y).toBe(2);
    },
  },
  V19: { build: () => [[0]], assertRead: (value) => expect(value).toEqual([[0]]) },
  V20: {
    build: () =>
      new LevelMapHolder(
        new Map([
          [Level.High, "h"],
          [Level.Low, "l"],
        ]),
      ),
    assertRead: (value) => {
      const holder = expectClass(value, LevelMapHolder);

      expect(holder.byLevel.get(Level.High)).toBe("h");
      expect(holder.byLevel.get(Level.Low)).toBe("l");
    },
  },
  V21: {
    build: () => new GuidMapHolder(new Map([[UUID.create(V21_GUID), "a"]])),
    assertRead: (value) => {
      const holder = expectClass(value, GuidMapHolder);

      expect(holder.byId.get(UUID.create(V21_GUID))).toBe("a");
    },
  },
  N1: { build: () => null, assertRead: (value) => expect(value).toBeNull() },
  N2: { build: () => null, assertRead: (value) => expect(value).toBeNull() },
  N5: { viaText: true },
  E8: { build: () => new UFailX(1) },
  E9: { build: () => new NLeaf(1, 2, 3) },
  E12: {
    build: () => {
      const members: Clip[] = [];
      const group = new Group("g", members);

      members.push(group);

      return group;
    },
  },
  E14: { build: () => new Other(3) },
  E15: { build: () => ({ nope: true }) },
  R9: { assertRead: (value) => expect(groupDepth(value)).toBe(32) },
  E24: { viaText: true },
  D2a: {
    assertRead: (value) => expectDecimal(value, "12345678901234567890.123456789"),
    viaText: true,
  },
  D2b: {
    assertRead: (value) => expectDecimal(value, "0.3333333333333333333333333333"),
    viaText: true,
  },
  D4: { viaText: true },
  W14: {
    build: () => new ImageClip("a", 1.5),
    assertRead: (value) => {
      const clip = expectClass(value, ImageClip);

      expect(clip.path).toBe("a");
      expect(clip.seconds).toBe(1.5);
    },
  },
  WEB1: {
    assertRead: (value) => {
      const clip = expectClass(value, ImageClip);

      expect(clip.path).toBe("a");
      expect(clip.seconds).toBe(1);
    },
  },
  WEB2: {
    assertRead: (value) => {
      const clip = expectClass(value, ImageClip);

      expect(clip.path).toBe("b");
      expect(clip.seconds).toBe(1);
    },
  },
  WEB3: {
    assertRead: (value) => {
      const nums = value as Nums;

      expect(nums.n).toBe(5);
      expect(nums.m).toBe(7);
      expectDecimal(nums.d, "1.1");
      expect(nums.l).toEqual([1, 2]);
      expect(nums.map?.get("k")).toBe(3);
    },
    viaText: true,
  },
  WEB3b: {
    assertRead: (value) => expectDecimal((value as Nums).d, "12345678901234567890.123456789"),
    viaText: true,
  },
  WEB4a: {
    assertRead: (value) => {
      const nums = expectClass(value, SoftNums);

      expect(nums.n).toBe(5);
      expect(nums.d).toBeNull();
    },
  },
  WEB4b: { assertRead: (value) => expect(expectClass(value, SoftNums).n).toBe(5) },
  WEB4c: { assertRead: (value) => expect(expectClass(value, SoftNums).n).toBe(7) },
  WEB4d: { assertRead: (value) => expect(expectClass(value, SoftNums).n).toBe(-5) },
  WEB5a: { assertRead: (value) => expect(expectClass(value, SoftNums).x).toBe(100) },
  WEB5b: { assertRead: (value) => expect(expectClass(value, SoftNums).x).toBe(1.5) },
  WEB5c: {
    assertRead: (value) => expect(Number.isNaN(expectClass(value, SoftNums).x)).toBe(true),
  },
  WEB5d: {
    assertRead: (value) => expect(expectClass(value, SoftNums).x).toBe(Number.POSITIVE_INFINITY),
  },
  WEB5e: {
    assertRead: (value) => expect(expectClass(value, SoftNums).x).toBe(Number.NEGATIVE_INFINITY),
  },
  WEB6: { assertRead: (value) => expect(value).toBe(5) },
  // The list and map parameters are non-nullable in C#, but WEB7's partial
  // value leaves them null, as the C# builder does.
  WEB7: {
    build: () => new Nums(5, 7, new Decimal("1.10"), 2.5, null!, null!, Color.Red),
    // Checks the crossReadable read of STJ's text (`"d":1.10`).
    assertRead: (value) => {
      const nums = expectClass(value, Nums);

      expectDecimal(nums.d, "1.1");
      expect(nums.x).toBe(2.5);
    },
  },
  NAM1: {
    build: () => new Stats(1, 2.5, "u"),
    assertRead: (value) => {
      const stats = expectClass(value, Stats);

      expect(stats.userName).toBe("u");
    },
  },
  NAM2: {
    build: () => new Stats(1, 2.5, "u"),
    assertRead: (value) => {
      const stats = expectClass(value, Stats);

      expect(stats.userName).toBe("u");
    },
  },
  M1: {
    build: () => new Blob([1, 2, 3], [255, 254], [1, 2]),
    assertRead: (value) => {
      const blob = expectClass(value, Blob);

      expect(blob.data).toEqual([1, 2, 3]);
      expect(blob.maybe).toEqual([255, 254]);
      expect(blob.numbers).toEqual([1, 2]);
    },
  },
  M2: {
    build: () => Object.assign(new Account(), { name: "n", version: 3, id: "x", hits: 4 }),
    assertRead: (value) => {
      const account = expectClass(value, Account);

      expect(account.name).toBe("n");
      expect(account.version).toBe(3);
      expect(account.id).toBe("x");
      expect(account.hits).toBe(4);
    },
  },
  M3: {
    build: () => new OrdBase(5, 1, 3),
    assertRead: (value) => {
      const ord = expectClass(value, OrdBase);

      expect(ord.first).toBe(1);
      expect(ord.second).toBe(5);
      expect(ord.third).toBe(3);
    },
  },
  M4: {
    build: () => Object.assign(new InitRec(1), { note: "n" }),
    assertRead: (value) => {
      const rec = expectClass(value, InitRec);

      expect(rec.a).toBe(1);
      expect(rec.note).toBe("n");
    },
  },
  M5: {
    build: () => Object.assign(new SetClass(), { n: 1 }),
    assertRead: (value) => {
      const set = expectClass(value, SetClass);

      expect(set.n).toBe(1);
      expect(set.count).toBe(5);
    },
  },
  W16: { assertRead: (value) => expect(value).toBe("ok") },
  W16b: {
    build: () => new EpisodeRec("e", Title.from("  ok ")),
    assertRead: (value) => {
      const rec = expectClass(value, EpisodeRec);

      expect(rec.id).toBe("e");
      expect<string>(rec.note).toBe("ok");
    },
  },
  T1: {
    assertRead: (value) => expect(String(value)).toBe("03:04:05"),
  },
  T2: {
    assertRead: (value) => expect(String(value)).toBe("03:04:05"),
  },
  T3: {
    assertRead: (value) => expect(String(value)).toBe("00:00:00"),
  },
  T4: {
    assertRead: (value) => expect(String(value)).toBe("03:04:05.1234567"),
  },
  T12: {
    assertRead: (value) => expect(String(value)).toBe("2024-02-29"),
  },
  T15: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T00:00:00"),
  },
  T16: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T10:20:00"),
  },
  T17: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T10:20:30"),
  },
  T18: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T10:20:30.1234567"),
  },
  T22: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T10:20:30+02:00[+02:00]"),
  },
  T41: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T10:20:30.9999999"),
  },
  T43: {
    assertRead: (value) => {
      // A DateTime read of an explicit offset is the instant's local wall
      // time (STJ's Kind=Local read), so the expectation is computed.
      const instant = new Date(Date.parse("2024-01-31T10:20:30+14:00"));
      const pad = (field: number) => String(field).padStart(2, "0");
      expect(String(value)).toBe(
        `${instant.getFullYear()}-${pad(instant.getMonth() + 1)}-${pad(instant.getDate())}` +
          `T${pad(instant.getHours())}:${pad(instant.getMinutes())}:${pad(instant.getSeconds())}`,
      );
    },
  },
  T45: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T10:20:30+14:00[+14:00]"),
  },
  T47: {
    assertRead: (value) => expect(String(value)).toBe("2024-01-31T10:20:30.9999999+02:00[+02:00]"),
  },
  T25: {
    assertRead: (value) => expect(String(value)).toBe("PT10H20M"),
  },
  T26: {
    assertRead: (value) => expect(String(value)).toBe("P10D"),
  },
  T27: {
    assertRead: (value) => expect(String(value)).toBe("P1DT10H20M"),
  },
  T28: {
    assertRead: (value) => expect(String(value)).toBe("-P10675199DT2H48M5.4775808S"),
  },
  T33: {
    assertRead: (value) => {
      const schedule = expectClass(value, Schedule);

      expect(String(schedule.at)).toBe("2024-01-31T10:20:00");
      expect(String(schedule.starts)).toBe("03:04:05");
      expect(String(schedule.day)).toBe("2024-02-29");
      expect(String(schedule.duration)).toBe("P1DT10H20M30.5S");
      expect(String(schedule.moment)).toBe("2024-01-31T10:20:30+02:00[+02:00]");
    },
  },
  OK1: {
    assertRead: (value) => expect(value).toEqual([1, null]),
  },
  OK2: {
    assertRead: (value) => expect((value as Map<string, number | null>).get("a")).toBeNull(),
  },
  OK3: {
    assertRead: (value) => expect(value).toEqual(["a", null]),
  },
  OK4: {
    assertRead: (value) => expect((value as { c: string }).c).toBe("a"),
  },
  // The 64-bit rows of spec 012 (slice 012e) run on the System.Text.Json
  // side only (#605 moves them here); I2 runs on both sides: the generated
  // fields are bigint values, which write and read the same digits.
  I2: {
    build: () => new Int64Box(42n, 7n, null),
    assertRead: (value) => {
      const box = expectClass(value, Int64Box);

      expect(box.id).toBe(42n);

      expect(box.mask).toBe(7n);

      expect(box.parent).toBeNull();
    },
  },
};

/** Every builder key must have a manifest row (drift fails the test). */
export function builderIds(): string[] {
  return Object.keys(caseBuilders);
}

function expectClass<T extends object>(value: unknown, ctor: new (...args: never[]) => T): T {
  expect(value).toBeInstanceOf(ctor);

  return value as T;
}

function expectPlainObject(value: unknown): void {
  expect(Object.getPrototypeOf(value)).toBe(Object.prototype);
}

function expectDecimal(value: unknown, text: string): void {
  expect(value).toBeInstanceOf(Decimal);

  expect((value as Decimal).toFixed()).toBe(text);
}

/** The number of nested groups of a read `Group` chain (manifest row R9). */
function groupDepth(value: unknown): number {
  return value instanceof Group ? 1 + groupDepth(value.members[0]) : 0;
}
