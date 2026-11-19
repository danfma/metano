/**
 * Runs the conformance rows `tests/BclReference` records from real .NET against the
 * `metano-runtime` helpers. A row names a function, its arguments and the outcome .NET
 * produced; the helpers must return the same value, or throw the same exception type for the
 * same parameter. Plain Node-compatible TypeScript (relative imports, no test framework), so
 * the Bun test and the Node runner share it.
 */

import { readFileSync } from "node:fs";

export type RowOutcome =
  | { readonly result: unknown }
  | { readonly error: string; readonly param: string | null; readonly message: string };

export type ConformanceRow = {
  readonly fn: string;
  readonly args: readonly unknown[];
} & RowOutcome;

export type RowFunction = (...args: never[]) => unknown;

export type RowRegistry = Readonly<Record<string, RowFunction>>;

const fixtureDirectory = new URL("./", import.meta.url);

export function readRows(file: string): readonly ConformanceRow[] {
  return JSON.parse(readFileSync(new URL(file, fixtureDirectory), "utf8")) as ConformanceRow[];
}

/**
 * The mismatches between the rows of `file` and the registry's functions; empty when they all
 * agree.
 */
export function checkRows(file: string, registry: RowRegistry): string[] {
  return readRows(file).flatMap((row) => {
    const mismatch = compareRow(row, registry);

    return mismatch === null ? [] : [`${describeRow(row)}: ${mismatch}`];
  });
}

function describeRow(row: ConformanceRow): string {
  return `${row.fn}(${row.args.map((arg) => JSON.stringify(arg)).join(", ")})`;
}

function compareRow(row: ConformanceRow, registry: RowRegistry): string | null {
  const run = registry[row.fn];

  if (run === undefined) {
    return `no function ${row.fn} in the registry`;
  }

  const outcome = attempt(() => (run as (...args: unknown[]) => unknown)(...row.args.map(decode)));

  return "error" in row ? compareError(row, outcome) : compareValue(row, outcome);
}

type Attempt = { readonly value: unknown } | { readonly thrown: unknown };

function attempt(run: () => unknown): Attempt {
  try {
    return { value: run() };
  } catch (thrown) {
    return { thrown };
  }
}

function compareValue(row: ConformanceRow & { result: unknown }, outcome: Attempt): string | null {
  if ("thrown" in outcome) {
    return `threw ${String(outcome.thrown)}, .NET returned ${JSON.stringify(row.result)}`;
  }

  const expected = show(decode(row.result));
  const actual = show(outcome.value);

  return expected === actual ? null : `got ${actual}, .NET gives ${expected}`;
}

function compareError(
  row: ConformanceRow & { error: string; param: string | null; message: string },
  outcome: Attempt,
): string | null {
  if ("value" in outcome) {
    return `returned ${show(outcome.value)}, .NET throws ${row.error}`;
  }

  const thrown = outcome.thrown as { name?: string; paramName?: string | null; message?: string };

  if (thrown.name !== row.error) {
    return `threw ${thrown.name}, .NET throws ${row.error}`;
  }

  if ((thrown.paramName ?? null) !== row.param) {
    return `parameter ${thrown.paramName ?? null}, .NET reports ${row.param}`;
  }

  return thrown.message === row.message
    ? null
    : `message "${thrown.message}", .NET says "${row.message}"`;
}

/**
 * A row value as the helper receives it: `{"bigint"}` as a `bigint`, `{"undefined"}` as
 * `undefined`, `{"double"}` as a non-finite or negative-zero number (JSON has none).
 */
function decode(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(decode);
  }

  if (typeof value === "object" && value !== null) {
    if ("bigint" in value) {
      return BigInt(value.bigint as string);
    }

    if ("undefined" in value) {
      return undefined;
    }

    if ("double" in value) {
      return Number(value.double === "-0" ? -0 : value.double);
    }
  }

  return value;
}

function show(value: unknown): string {
  return JSON.stringify(value, (_key, item) =>
    typeof item === "bigint" ? `${item}n` : Object.is(item, -0) ? "-0" : item,
  );
}
