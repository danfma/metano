/**
 * The TypeScript half of the spec 012 reference conformance (FR-064): reads a
 * fixture that tests/Int64Reference wrote on .NET and runs every case through
 * the transpiled twin, comparing the result, or the exception's name and
 * message, with what .NET produced. Shared by the bun test and the Node run,
 * so it imports nothing runtime-specific beyond `node:fs` / `node:path`.
 */

import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";

import { Decimal } from "decimal.js";

/** A fixture file: the twin signatures and one case per operand combination. */
export type Fixture = {
  readonly signatures: Readonly<Record<string, Signature>>;
  readonly cases: readonly FixtureCase[];
};

/** The .NET type names of a twin method's parameters and result (`Int64`, `Int64?`, …). */
export type Signature = {
  readonly parameters: readonly string[];
  readonly returns: string;
};

/** One call: its .NET result (null for a null result), or the exception it threw. */
export type FixtureCase = {
  readonly case: string;
  readonly args: readonly (string | null)[];
  readonly result?: string | null;
  readonly throws?: string;
  readonly message?: string;
};

/** A case whose outcome differs from .NET's. */
export type Mismatch = {
  readonly case: string;
  readonly args: readonly (string | null)[];
  readonly expected: string;
  readonly actual: string;
};

/** The module namespaces and static classes a fixture's methods are exported from. */
export type Twin = readonly object[];

type TwinFunction = (...args: unknown[]) => unknown;

type Outcome =
  | { readonly kind: "returns"; readonly value: unknown }
  | { readonly kind: "throws"; readonly name: string; readonly message: string };

const FIXTURES_PATH = join("tests", "Int64Reference", "fixtures");

const BIGINT_TYPES = new Set(["Int64", "UInt64", "BigInteger"]);

const INTEGER_NUMBER_TYPES = new Set(["Int32", "UInt32", "Int16", "UInt16", "SByte", "Byte"]);

const FLOATING_POINT_TYPES = new Set(["Double", "Single"]);

/** Reads `name` from the repository's fixture directory, found above this file. */
export function loadFixture(name: string): Fixture {
  return JSON.parse(readFileSync(join(fixturesDirectory(), name), "utf8")) as Fixture;
}

/** Every case of `fixture` whose outcome through `twin` differs from .NET's. */
export function mismatchesOf(fixture: Fixture, twin: Twin): Mismatch[] {
  return fixture.cases.flatMap((row) => {
    const signature = fixture.signatures[row.case];
    const twinFunction = twinFunctionOf(twin, exportedNameOf(row.case));

    if (signature === undefined || !isTwinFunction(twinFunction)) {
      return [mismatch(row, "a twin function", "no export")];
    }

    const outcome = invoke(twinFunction, signature, row.args);

    return matches(row, signature.returns, outcome)
      ? []
      : [mismatch(row, describeExpected(row), describeOutcome(outcome))];
  });
}

function fixturesDirectory(): string {
  return join(fixturesRootAbove(import.meta.dirname), FIXTURES_PATH);
}

/** The nearest directory at or above `directory` that holds the fixture directory. */
function fixturesRootAbove(directory: string): string {
  if (existsSync(join(directory, FIXTURES_PATH))) {
    return directory;
  }

  const parent = dirname(directory);

  if (parent === directory) {
    throw new Error(`No ${FIXTURES_PATH} above ${import.meta.dirname}.`);
  }

  return fixturesRootAbove(parent);
}

function isTwinFunction(value: unknown): value is TwinFunction {
  return typeof value === "function";
}

function twinFunctionOf(twin: Twin, name: string): unknown {
  const owner = twin.find((candidate) => name in candidate);

  return owner === undefined ? undefined : (owner as Record<string, unknown>)[name];
}

/** A .NET type name without its nullable marker (`Int64?` → `Int64`). */
function baseTypeOf(type: string): string {
  return type.replace("?", "");
}

/** C#'s `PascalCase` method name as the transpiled `camelCase` export. */
function exportedNameOf(method: string): string {
  return method.charAt(0).toLowerCase() + method.slice(1);
}

function invoke(
  twinFunction: TwinFunction,
  signature: Signature,
  args: readonly (string | null)[],
): Outcome {
  const values = args.map((text, index) => argumentOf(text, signature.parameters[index] ?? ""));

  try {
    return { kind: "returns", value: twinFunction(...values) };
  } catch (error) {
    return error instanceof Error
      ? { kind: "throws", name: error.name, message: error.message }
      : { kind: "throws", name: typeof error, message: String(error) };
  }
}

/** A fixture argument as the twin receives it: a `bigint`, a `number`, a one-character string or a `Decimal`. */
function argumentOf(text: string | null, type: string): unknown {
  if (text === null) {
    return null;
  }

  const baseType = baseTypeOf(type);

  if (BIGINT_TYPES.has(baseType)) {
    return BigInt(text);
  }

  if (baseType === "Char") {
    return String.fromCharCode(Number(text));
  }

  if (baseType === "Decimal") {
    return new Decimal(text);
  }

  return Number(text);
}

function matches(row: FixtureCase, returns: string, outcome: Outcome): boolean {
  if (row.throws !== undefined) {
    return (
      outcome.kind === "throws" && outcome.name === row.throws && outcome.message === row.message
    );
  }

  return outcome.kind === "returns" && returnsExpected(outcome.value, row.result ?? null, returns);
}

/** Whether a returned value is .NET's result, with the representation its C# type maps to. */
function returnsExpected(value: unknown, expected: string | null, type: string): boolean {
  if (expected === null) {
    return value === null;
  }

  const baseType = baseTypeOf(type);

  if (BIGINT_TYPES.has(baseType)) {
    return typeof value === "bigint" && value.toString() === expected;
  }

  if (INTEGER_NUMBER_TYPES.has(baseType)) {
    return typeof value === "number" && Number.isInteger(value) && String(value) === expected;
  }

  if (FLOATING_POINT_TYPES.has(baseType)) {
    return typeof value === "number" && Object.is(value, Number(expected));
  }

  if (baseType === "Char") {
    return (
      typeof value === "string" && value.length === 1 && String(value.charCodeAt(0)) === expected
    );
  }

  if (baseType === "Decimal") {
    return value instanceof Decimal && value.equals(new Decimal(expected));
  }

  return String(value) === expected;
}

function describeExpected(row: FixtureCase): string {
  return row.throws === undefined
    ? `returns ${row.result ?? "null"}`
    : `throws ${row.throws}: ${row.message ?? ""}`;
}

function describeOutcome(outcome: Outcome): string {
  if (outcome.kind === "throws") {
    return `throws ${outcome.name}: ${outcome.message}`;
  }

  const value = outcome.value;

  return `returns ${value instanceof Decimal ? value.toFixed() : String(value)} (${typeof value})`;
}

function mismatch(row: FixtureCase, expected: string, actual: string): Mismatch {
  return { case: row.case, args: row.args, expected, actual };
}
