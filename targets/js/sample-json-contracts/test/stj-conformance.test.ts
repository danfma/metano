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
import { Group } from "#/sample-json-contracts/group";
import { HLeaf } from "#/sample-json-contracts/h-leaf";
import { HMid } from "#/sample-json-contracts/h-mid";
import { IA } from "#/sample-json-contracts/ia";
import { IgnBase } from "#/sample-json-contracts/ign-base";
import { IgnoreJsonContext } from "#/sample-json-contracts/ignore-json-context";
import { ImageClip } from "#/sample-json-contracts/image-clip";
import { InitRec } from "#/sample-json-contracts/init-rec";
import { IntMapHolder } from "#/sample-json-contracts/int-map-holder";
import { IS } from "#/sample-json-contracts/is";
import { KebabCaseJsonContext } from "#/sample-json-contracts/kebab-case-json-context";
import { MembersJsonContext } from "#/sample-json-contracts/members-json-context";
import { MixedClass } from "#/sample-json-contracts/mixed-class";
import { NLeaf } from "#/sample-json-contracts/n-leaf";
import { NMid } from "#/sample-json-contracts/n-mid";
import { ODer } from "#/sample-json-contracts/o-der";
import { OrdBase } from "#/sample-json-contracts/ord-base";
import { OutOfOrderJsonContext } from "#/sample-json-contracts/out-of-order-json-context";
import { SBase } from "#/sample-json-contracts/s-base";
import { SetClass } from "#/sample-json-contracts/set-class";
import { SilenceClip } from "#/sample-json-contracts/silence-clip";
import { SnakeCaseJsonContext } from "#/sample-json-contracts/snake-case-json-context";
import { Sq } from "#/sample-json-contracts/sq";
import { SqB } from "#/sample-json-contracts/sq-b";
import { Stats } from "#/sample-json-contracts/stats";
import { StudioJsonContext } from "#/sample-json-contracts/studio-json-context";
import { Timeline } from "#/sample-json-contracts/timeline";
import { UNearA } from "#/sample-json-contracts/u-near-a";
import { WebJsonContext } from "#/sample-json-contracts/web-json-context";
import { builderIds, type CaseBuilder, caseBuilders } from "./builders";
import manifest from "./fixtures/stj/cases.json";

/**
 * The TypeScript half of the shared System.Text.Json conformance manifest
 * (spec 009, User Story 5, contracts/wire-format.md). Every row whose
 * `sides` include `ts` runs against the generated contexts: write and
 * roundtrip rows compare `JSON.stringify` of the serialized tree byte for
 * byte, read and roundtrip rows assert the expected class and the builder's
 * field values, and error rows assert the thrown class and the whole
 * message, normalized as the STJ side normalizes it. Rows with a
 * `divergence` note carry the STJ text in `stjJson`; the TypeScript side
 * compares its own `json` text and, when the row is `crossReadable`, also
 * reads `stjJson` back to the same value and text.
 */
type Side = "stj" | "ts";

interface ConformanceError {
  readonly class: string;
  readonly message?: string;
  readonly stjMessage?: string;
  readonly tsMessage?: string;
  readonly stjPath?: string;
}

interface ConformanceCase {
  readonly id: string;
  readonly context: string;
  readonly getter: string;
  readonly direction: "roundtrip" | "write" | "read";
  readonly sides?: readonly Side[];
  readonly expectedClass?: string;
  readonly json?: string;
  readonly stjJson?: string;
  readonly crossReadable?: boolean;
  readonly error?: ConformanceError;
  readonly divergence?: string;
}

/** STJ prints the sample's namespace in some messages; both sides drop it. */
const SAMPLE_NAMESPACE = "SampleJsonContracts.";

const NO_BUILDER: CaseBuilder = {};

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

const errorClasses = { JsonException, NotSupportedException } as const;

const expectedClasses: Record<string, new (...args: never[]) => object> = {
  Account,
  Blob,
  DBer,
  DBase,
  EpisodeRec,
  EpisodeSummary,
  Feed,
  GDer,
  Group,
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
  SqB,
  Stats,
  Timeline,
  UNearA,
};

describe("STJ conformance manifest", () => {
  test("every ts row that needs a builder has one", () => {
    const missing = tsCases()
      .filter(requiresBuilder)
      .map((row) => row.id)
      .filter((id) => caseBuilders[id] === undefined);

    expect(missing).toEqual([]);
  });

  test("every ts builder has a manifest row", () => {
    const rowIds = new Set(tsCases().map((row) => row.id));

    expect(builderIds().filter((id) => !rowIds.has(id))).toEqual([]);
  });

  test("every row with an STJ text says whether the sides read each other's text", () => {
    const undeclared = cases
      .filter((row) => row.stjJson !== undefined && row.crossReadable === undefined)
      .map((row) => row.id);

    expect(undeclared).toEqual([]);
  });

  for (const row of tsCases()) {
    test(`${row.id} ${row.direction} ${row.context}.${row.getter}`, () => {
      runTsCase(row);
    });
  }
});

function tsCases(): readonly ConformanceCase[] {
  return cases.filter((row) => runsOn(row, "ts"));
}

function runsOn(row: ConformanceCase, side: Side): boolean {
  return row.sides === undefined || row.sides.includes(side);
}

/**
 * Every row needs a builder except a read error: its input is the manifest
 * text and its expectation the manifest error. Such a row keeps a builder
 * only for a flag (`viaText`).
 */
function requiresBuilder(row: ConformanceCase): boolean {
  return !(row.error !== undefined && row.direction === "read");
}

function runTsCase(row: ConformanceCase): void {
  const spec = specOf(row);
  const builder = caseBuilders[row.id] ?? NO_BUILDER;

  if (row.error !== undefined) {
    expectError(row, row.error, builder, spec);

    return;
  }

  const text = row.direction === "read" ? jsonOf(row) : expectWrittenText(row, builder, spec);

  if (row.direction !== "write") {
    expectReadValue(row, builder, spec, text);
  }

  if (row.crossReadable === true) {
    expectCrossRead(row, builder, spec);
  }
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

function jsonOf(row: ConformanceCase): string {
  if (row.json === undefined) {
    throw new Error(`manifest case '${row.id}' has no json text`);
  }

  return row.json;
}

/** Serializes the builder's value once; a roundtrip row then reads this text. */
function expectWrittenText(row: ConformanceCase, builder: CaseBuilder, spec: unknown): string {
  const text = writeText(requireBuild(row, builder), spec);

  expect(text).toBe(jsonOf(row));

  return text;
}

function expectReadValue(
  row: ConformanceCase,
  builder: CaseBuilder,
  spec: unknown,
  text: string,
): unknown {
  const value = readValue(text, spec, builder);

  assertExpectedClass(row, value, builder);
  builder.assertRead?.(value);

  return value;
}

/** The TS side reads STJ's text to the same value, and writes its own text back. */
function expectCrossRead(row: ConformanceCase, builder: CaseBuilder, spec: unknown): void {
  if (row.stjJson === undefined) {
    throw new Error(`crossReadable case '${row.id}' has no stjJson text`);
  }

  const value = expectReadValue(row, builder, spec, row.stjJson);

  expect(writeText(value, spec)).toBe(jsonOf(row));
}

function expectError(
  row: ConformanceCase,
  error: ConformanceError,
  builder: CaseBuilder,
  spec: unknown,
): void {
  const thrown = caught(() =>
    row.direction === "read"
      ? readValue(jsonOf(row), spec, builder)
      : writeText(requireBuild(row, builder), spec),
  );

  expect(thrown).toBeInstanceOf(errorClassOf(row, error));
  expect(normalizedMessage(thrown as Error)).toBe(expectedTsMessage(row, error));
}

function expectedTsMessage(row: ConformanceCase, error: ConformanceError): string {
  const message = error.tsMessage ?? error.message;

  if (message === undefined) {
    throw new Error(`error case '${row.id}' has neither a message nor a tsMessage`);
  }

  return message;
}

function errorClassOf(row: ConformanceCase, error: ConformanceError): new () => Error {
  const errorClass = errorClasses[error.class as keyof typeof errorClasses];

  if (errorClass === undefined) {
    throw new Error(`unknown error class '${error.class}' in manifest case '${row.id}'`);
  }

  return errorClass;
}

function normalizedMessage(error: Error): string {
  return error.message.replaceAll(SAMPLE_NAMESPACE, "");
}

function caught(action: () => unknown): unknown {
  try {
    action();
  } catch (error) {
    return error;
  }

  return undefined;
}

function requireBuild(row: ConformanceCase, builder: CaseBuilder): unknown {
  if (builder.build === undefined) {
    throw new Error(`no value builder for ${row.direction} case '${row.id}'`);
  }

  return builder.build();
}

function writeText(value: unknown, spec: unknown): string {
  return JSON.stringify(JsonSerializer.serialize(value as never, spec as never));
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
