import { describe, expect, test } from "bun:test";

import { JsonException, JsonSerializer, NotSupportedException } from "metano-runtime/system/json";

import { Account } from "#/sample-json-contracts/account";
import { Blob } from "#/sample-json-contracts/blob";
import { DBer } from "#/sample-json-contracts/d-ber";
import { DBase } from "#/sample-json-contracts/d-base";
import { EpisodeRec } from "#/sample-json-contracts/episode-rec";
import { EpisodeSummary } from "#/sample-json-contracts/episode-summary";
import { Feed } from "#/sample-json-contracts/feed";
import { GDer } from "#/sample-json-contracts/g-der";
import { HLeaf } from "#/sample-json-contracts/h-leaf";
import { HMid } from "#/sample-json-contracts/h-mid";
import { IA } from "#/sample-json-contracts/ia";
import { IgnBase } from "#/sample-json-contracts/ign-base";
import { ImageClip } from "#/sample-json-contracts/image-clip";
import { IgnoreJsonContext } from "#/sample-json-contracts/ignore-json-context";
import { InitRec } from "#/sample-json-contracts/init-rec";
import { IntMapHolder } from "#/sample-json-contracts/int-map-holder";
import { IS } from "#/sample-json-contracts/is";
import { KebabCaseJsonContext } from "#/sample-json-contracts/kebab-case-json-context";
import { MembersJsonContext } from "#/sample-json-contracts/members-json-context";
import { MixedClass } from "#/sample-json-contracts/mixed-class";
import { NLeaf } from "#/sample-json-contracts/n-leaf";
import { NMid } from "#/sample-json-contracts/n-mid";
import { OrdBase } from "#/sample-json-contracts/ord-base";
import { OutOfOrderJsonContext } from "#/sample-json-contracts/out-of-order-json-context";
import { ODer } from "#/sample-json-contracts/o-der";
import { SBase } from "#/sample-json-contracts/s-base";
import { SetClass } from "#/sample-json-contracts/set-class";
import { SilenceClip } from "#/sample-json-contracts/silence-clip";
import { SnakeCaseJsonContext } from "#/sample-json-contracts/snake-case-json-context";
import { Sq } from "#/sample-json-contracts/sq";
import { Stats } from "#/sample-json-contracts/stats";
import { StudioJsonContext } from "#/sample-json-contracts/studio-json-context";
import { Timeline } from "#/sample-json-contracts/timeline";
import { UNearA } from "#/sample-json-contracts/u-near-a";
import { WebJsonContext } from "#/sample-json-contracts/web-json-context";
import manifest from "./fixtures/stj/cases.json";
import { builderFor, builderIds, type CaseBuilder } from "./builders";

/**
 * The TypeScript half of the shared System.Text.Json conformance manifest
 * (spec 009, User Story 5, contracts/wire-format.md). Every row whose
 * `sides` include `ts` runs against the generated contexts: write and
 * roundtrip rows compare `JSON.stringify` of the serialized tree byte for
 * byte, read and roundtrip rows assert the expected class and the builder's
 * field values, and error rows assert the thrown class and message prefix.
 * Rows marked `divergent` carry the STJ text in `stjJson`; the TypeScript
 * side always compares its own `json` text.
 */
interface ConformanceCase {
  readonly id: string;
  readonly context: string;
  readonly getter: string;
  readonly direction: "roundtrip" | "write" | "read";
  readonly sides?: readonly ("stj" | "ts")[];
  readonly expectedClass?: string;
  readonly json?: string;
  readonly stjJson?: string;
  readonly error?: { readonly class: string; readonly message: string };
  readonly divergence?: string;
}

const cases = manifest as readonly ConformanceCase[];

const contexts: Record<string, unknown> = {
  StudioJsonContext: StudioJsonContext.default,
  WebJsonContext: WebJsonContext.default,
  OutOfOrderJsonContext: OutOfOrderJsonContext.default,
  IgnoreJsonContext: IgnoreJsonContext.default,
  SnakeCaseJsonContext: SnakeCaseJsonContext.default,
  KebabCaseJsonContext: KebabCaseJsonContext.default,
  MembersJsonContext: MembersJsonContext.default,
};

const expectedClasses: Record<string, new (...args: never[]) => object> = {
  Account,
  Blob,
  DBer,
  DBase,
  EpisodeRec,
  EpisodeSummary,
  Feed,
  GDer,
  HLeaf,
  HMid,
  IA,
  IgnBase,
  ImageClip,
  InitRec,
  IntMapHolder,
  IS,
  MixedClass,
  NLeaf,
  NMid,
  ODer,
  OrdBase,
  SBase,
  SetClass,
  SilenceClip,
  Sq,
  Stats,
  Timeline,
  UNearA,
};

describe("STJ conformance manifest", () => {
  test("every ts manifest row has a builder", () => {
    for (const row of tsCases()) {
      builderFor(row.id);
    }
  });

  test("every ts builder has a manifest row", () => {
    const rowIds = new Set(tsCases().map((row) => row.id));

    for (const id of builderIds()) {
      expect(rowIds.has(id)).toBe(true);
    }
  });

  for (const row of tsCases()) {
    test(`${row.id} ${row.direction} ${row.context}.${row.getter}`, () => {
      runTsCase(row);
    });
  }
});

function tsCases(): readonly ConformanceCase[] {
  return cases.filter((row) => sidesOf(row).includes("ts"));
}

function sidesOf(row: ConformanceCase): readonly ("stj" | "ts")[] {
  return row.sides ?? ["stj", "ts"];
}

function specOf(row: ConformanceCase): unknown {
  const context = contexts[row.context];

  if (context === undefined) {
    throw new Error(`no generated context '${row.context}' for manifest case '${row.id}'`);
  }

  const getter = (context as Record<string, unknown>)[row.getter];

  if (getter === undefined) {
    throw new Error(`context '${row.context}' has no getter '${row.getter}' (${row.id})`);
  }

  return getter;
}

function runTsCase(row: ConformanceCase): void {
  const builder = builderFor(row.id);
  const spec = specOf(row);

  if (row.error !== undefined) {
    expectError(row, builder, spec);

    return;
  }

  if (row.direction === "write" || row.direction === "roundtrip") {
    const text = JSON.stringify(serializeValue(requireBuild(row, builder), spec));

    expect(text).toBe(row.json);
  }

  if (row.direction === "read" || row.direction === "roundtrip") {
    const input = row.direction === "read" ? row.json! : JSON.stringify(serializeValue(requireBuild(row, builder), spec));
    const value = readValue(input, spec, builder);

    assertExpectedClass(row, value, builder);
    builder.assertRead?.(value);
  }
}

function requireBuild(row: ConformanceCase, builder: CaseBuilder): unknown {
  if (builder.build === undefined) {
    throw new Error(`no value builder for ${row.direction} case '${row.id}'`);
  }

  return builder.build();
}

function serializeValue(value: unknown, spec: unknown): unknown {
  return JsonSerializer.serialize(value as never, spec as never);
}

function readValue(text: string, spec: unknown, builder: CaseBuilder): unknown {
  if (builder.viaText) {
    return JsonSerializer.deserializeText(text, spec as never);
  }

  return JsonSerializer.deserialize(JSON.parse(text), spec as never);
}

function assertExpectedClass(row: ConformanceCase, value: unknown, builder: CaseBuilder): void {
  if (row.expectedClass === undefined || builder.plainObject) {
    return;
  }

  const ctor = expectedClasses[row.expectedClass];

  if (ctor === undefined) {
    throw new Error(`no TS class '${row.expectedClass}' for manifest case '${row.id}'`);
  }

  expect(value).toBeInstanceOf(ctor);
}

function expectError(row: ConformanceCase, builder: CaseBuilder, spec: unknown): void {
  const errorClass = row.error!.class === "JsonException" ? JsonException : NotSupportedException;

  let thrown: unknown;

  try {
    if (row.direction === "read") {
      readValue(row.json!, spec, builder);
    } else {
      serializeValue(requireBuild(row, builder), spec);
    }
  } catch (thrownError) {
    thrown = thrownError;
  }

  expect(thrown).toBeInstanceOf(errorClass);

  if (!(thrown as Error).message.startsWith(row.error!.message)) console.log(`ACTUAL ${row.id}:`, (thrown as Error).message);

  expect((thrown as Error).message.startsWith(row.error!.message)).toBe(true);
}
