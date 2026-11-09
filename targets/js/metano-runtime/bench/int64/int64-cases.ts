/**
 * The 64-bit benchmark cases (spec 012 FR-066): each hot path the feature
 * touches, next to the same code over `number`, so the reported figure is
 * the ratio between them. The harness (int64-probe.ts) samples each case
 * the plan's method: median of 10 runs after 3 warm-ups.
 */

import { divInt64, hashInt64 } from "../../src/system/int64.ts";
import { formatInt64 } from "../../src/system/int64-format.ts";
import { parseInt64 } from "../../src/system/int64-parsing.ts";
import { JsonSerializer } from "../../src/system/json/index.ts";
import type { TypeSpec, ValueSpec } from "../../src/system/json/index.ts";

/** A record with a 64-bit id, as generated code declares it. */
export class SnowflakeRecord {
  constructor(
    readonly id: bigint,
    readonly key: string,
  ) {}
}

/** The same record over a `number` id, the 2.x shape. */
export class NumberRecord {
  constructor(
    readonly id: number,
    readonly key: string,
  ) {}
}

const snowflakeSpec: TypeSpec<SnowflakeRecord> = {
  type: SnowflakeRecord,
  factory: (p) => new SnowflakeRecord(p.id as bigint, p.key as string),
  properties: [
    { ts: "id", json: "id", type: { kind: "int64", numberType: "System.Int64" } },
    { ts: "key", json: "key", type: { kind: "primitive" } },
  ],
};

const numberSpec: TypeSpec<NumberRecord> = {
  type: NumberRecord,
  factory: (p) => new NumberRecord(p.id as number, p.key as string),
  properties: [
    { ts: "id", json: "id", type: { kind: "primitive" } },
    { ts: "key", json: "key", type: { kind: "primitive" } },
  ],
};

const snowflakeRecordsSpec: ValueSpec<SnowflakeRecord[]> = {
  descriptor: { kind: "array", element: { kind: "ref", spec: () => snowflakeSpec } },
};

const numberRecordsSpec: ValueSpec<NumberRecord[]> = {
  descriptor: { kind: "array", element: { kind: "ref", spec: () => numberSpec } },
};

const SUM_ITERATIONS = 1_000_000;

const HASH_STRINGS = 100_000;

const FORMAT_VALUES = 100_000;

const RECORDS = 100_000;

const sumBigints: readonly bigint[] = Array.from(
  { length: 10_000 },
  (_, index) => BigInt(index * 1_000_003) - 5_000_000_000n,
);

const sumNumbers: readonly number[] = sumBigints.map((value) => Number(value));

const hashInputs: readonly string[] = Array.from(
  { length: HASH_STRINGS },
  (_, index) => `issue-${index}-${(index * 2654435761) % 4294967296}`,
);

const formatInputs: readonly bigint[] = Array.from(
  { length: FORMAT_VALUES },
  (_, index) => BigInt(index) * 1_000_003n - 5_000_000_000_000n,
);

const parseInputs: readonly string[] = formatInputs.map((value) => value.toString());

const snowflakeRecords: readonly SnowflakeRecord[] = Array.from(
  { length: RECORDS },
  (_, index) => new SnowflakeRecord((1n << 60n) + BigInt(index), `key-${index}`),
);

const numberRecords: readonly NumberRecord[] = Array.from(
  { length: RECORDS },
  (_, index) => new NumberRecord(2 ** 50 + index, `key-${index}`),
);

/** One benchmark case: the 64-bit run and its `number` baseline. */
export type Int64Case = {
  readonly name: string;
  readonly withInt64: () => unknown;
  readonly withNumber: () => unknown;
};

export const INT64_CASES: readonly Int64Case[] = [
  {
    name: "sum 1e6 (wrap vs plain)",
    withInt64: () => {
      let acc = 0n;

      for (let round = 0; round < SUM_ITERATIONS / sumBigints.length; round++) {
        for (const value of sumBigints) {
          acc = BigInt.asIntN(64, acc + value);
        }
      }

      return acc;
    },
    withNumber: () => {
      let acc = 0;

      for (let round = 0; round < SUM_ITERATIONS / sumNumbers.length; round++) {
        for (const value of sumNumbers) {
          acc += value;
        }
      }

      return acc;
    },
  },
  {
    name: "FNV-1a hash mix 1e5 strings",
    withInt64: () => {
      let hash = 0xcbf29ce484222325n;

      for (const text of hashInputs) {
        for (let index = 0; index < text.length; index++) {
          hash = BigInt.asUintN(64, (hash * 1099511628211n) ^ BigInt(text.charCodeAt(index)));
        }
      }

      return hashInt64(hash);
    },
    withNumber: () => {
      let hash = 2166136261 | 0;

      for (const text of hashInputs) {
        for (let index = 0; index < text.length; index++) {
          hash = Math.imul(hash, 16777619) ^ text.charCodeAt(index);
        }
      }

      return hash | 0;
    },
  },
  {
    name: "division 1e6 (helper + constant vs number)",
    withInt64: () => {
      let acc = 0n;

      for (const value of sumBigints) {
        acc += divInt64(value, 1_000_003n);
      }

      for (const value of sumBigints) {
        acc += value / 7n;
      }

      return acc;
    },
    withNumber: () => {
      let acc = 0;

      for (const value of sumNumbers) {
        acc += value / 7;
      }

      return acc;
    },
  },
  {
    name: 'formatInt64("N0") 1e5 vs toLocaleString',
    withInt64: () => {
      let acc = 0;

      for (const value of formatInputs) {
        acc += formatInt64(value, "N0").length;
      }

      return acc;
    },
    withNumber: () => {
      let acc = 0;

      for (const value of formatInputs) {
        acc += value.toLocaleString("en-US", { maximumFractionDigits: 0 }).length;
      }

      return acc;
    },
  },
  {
    name: "parseInt64 1e5 vs Number",
    withInt64: () => {
      let acc = 0n;

      for (const text of parseInputs) {
        acc += parseInt64(text);
      }

      return acc;
    },
    withNumber: () => {
      let acc = 0;

      for (const text of parseInputs) {
        acc += Number(text);
      }

      return acc;
    },
  },
  {
    name: "JSON round-trip 1e5 records",
    withInt64: () => {
      const text = JSON.stringify(JsonSerializer.serialize(snowflakeRecords, snowflakeRecordsSpec));

      return JsonSerializer.deserializeText(text, snowflakeRecordsSpec);
    },
    withNumber: () => {
      const text = JSON.stringify(JsonSerializer.serialize(numberRecords, numberRecordsSpec));

      return JsonSerializer.deserialize(JSON.parse(text), numberRecordsSpec);
    },
  },
];
