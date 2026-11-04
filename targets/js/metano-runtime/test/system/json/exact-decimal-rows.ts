import { Decimal } from "decimal.js";
import { JsonSerializer, type ValueSpec } from "#/system/json";

/**
 * What System.Text.Json writes for a `System.Decimal` (#458), measured with a
 * source-generated context on .NET 10.0.12 (System.Text.Json 10.0.0.0).
 * `text` builds the decimal.js value; `metano` is set only where decimal.js
 * cannot hold what STJ writes: the scale (`1.10m` is `1.1` in decimal.js).
 */
export type DecimalWriteRow = {
  readonly csharp: string;
  readonly text: string;
  readonly stj: string;
  readonly metano?: string;
};

export const DECIMAL_WRITE_ROWS: readonly DecimalWriteRow[] = [
  { csharp: "0m", text: "0", stj: "0" },
  { csharp: "1m", text: "1", stj: "1" },
  { csharp: "100m", text: "100", stj: "100" },
  { csharp: "-42m", text: "-42", stj: "-42" },
  { csharp: "19.99m", text: "19.99", stj: "19.99" },
  { csharp: "-0.5m", text: "-0.5", stj: "-0.5" },
  {
    csharp: "decimal.MaxValue",
    text: "79228162514264337593543950335",
    stj: "79228162514264337593543950335",
  },
  {
    csharp: "decimal.MinValue",
    text: "-79228162514264337593543950335",
    stj: "-79228162514264337593543950335",
  },
  {
    csharp: "12345678901234567890.123456789m",
    text: "12345678901234567890.123456789",
    stj: "12345678901234567890.123456789",
  },
  {
    csharp: "1.2345678901234567890123456789m",
    text: "1.2345678901234567890123456789",
    stj: "1.2345678901234567890123456789",
  },
  // decimal.js prints these with an exponent (`1e+21`, `1e+28`, `1e-7`, `1e-28`).
  { csharp: "1000000000000000000000m", text: "1e21", stj: "1000000000000000000000" },
  { csharp: "10000000000000000000000000000m", text: "1e28", stj: "10000000000000000000000000000" },
  { csharp: "0.0000001m", text: "1e-7", stj: "0.0000001" },
  {
    csharp: "0.0000000000000000000000000001m",
    text: "1e-28",
    stj: "0.0000000000000000000000000001",
  },
  {
    csharp: "-0.0000000000000000000000000001m",
    text: "-1e-28",
    stj: "-0.0000000000000000000000000001",
  },
  { csharp: 'decimal.Parse("1.5e3")', text: "1.5e3", stj: "1500" },
  // A negative zero keeps its sign bit in System.Decimal but is written `0`.
  { csharp: "decimal.Negate(0m)", text: "-0", stj: "0" },
  // The scale: decimal.js normalizes trailing zeros.
  { csharp: "1.10m", text: "1.10", stj: "1.10", metano: "1.1" },
  { csharp: "1.0m", text: "1.0", stj: "1.0", metano: "1" },
  { csharp: "0.00m", text: "0.00", stj: "0.00", metano: "0" },
  { csharp: "1000.000m", text: "1000.000", stj: "1000.000", metano: "1000" },
  { csharp: 'decimal.Parse("-0.00")', text: "-0.00", stj: "0.00", metano: "0" },
];

/**
 * Values at and past the edge of the `System.Decimal` domain (#458): a value
 * no `System.Decimal` holds throws `JsonException` before any text is built;
 * `written` is the text of an accepted one. A value below `1e-29` is written
 * `0`, as System.Text.Json reads `1e-40` and `1e-400` (measured), on every
 * path. `rejectedAs` is how the error prints a rejected value: plain
 * notation, except past the exponent `1e400000000` would expand to.
 */
export type DecimalDomainRow = {
  readonly text: string;
  readonly written?: string;
  readonly rejectedAs?: string;
};

export const DECIMAL_DOMAIN_ROWS: readonly DecimalDomainRow[] = [
  { text: "NaN", rejectedAs: "NaN" },
  { text: "Infinity", rejectedAs: "Infinity" },
  { text: "-Infinity", rejectedAs: "-Infinity" },
  { text: "1e29", rejectedAs: "100000000000000000000000000000" },
  { text: "-1e29", rejectedAs: "-100000000000000000000000000000" },
  { text: "79228162514264337593543950336", rejectedAs: "79228162514264337593543950336" },
  { text: "-79228162514264337593543950336", rejectedAs: "-79228162514264337593543950336" },
  { text: "1e400000000", rejectedAs: "1e+400000000" },
  { text: "79228162514264337593543950335", written: "79228162514264337593543950335" },
  { text: "-79228162514264337593543950335", written: "-79228162514264337593543950335" },
  { text: "79228162514264337593543950335.5", rejectedAs: "79228162514264337593543950335.5" },
  { text: "79228162514264337593543950335.4", written: "79228162514264337593543950335.4" },
  { text: "1e28", written: "10000000000000000000000000000" },
  { text: "1e-29", written: "0.00000000000000000000000000001" },
  { text: "9.99e-29", written: "0.0000000000000000000000000000999" },
  { text: "-9.99e-29", written: "-0.0000000000000000000000000000999" },
  { text: "9.99e-30", written: "0" },
  { text: "-9.99e-30", written: "0" },
  { text: "1e-40", written: "0" },
  { text: "1e-400000000", written: "0" },
  { text: "-1e-400000000", written: "0" },
];

/** The error a rejected row throws. */
export function domainErrorOf(row: DecimalDomainRow): string {
  return `JsonException: The value ${row.rejectedAs} cannot be written as System.Decimal: it is outside the System.Decimal range.`;
}

/** The text written for the row's value, or the error it throws. */
export function domainOutcomeOf(row: DecimalDomainRow): string {
  try {
    return JSON.stringify(JsonSerializer.serialize(new Decimal(row.text), decimalSpec));
  } catch (error) {
    const { name, message } = error as Error;

    return `${name}: ${message}`;
  }
}

export const decimalSpec: ValueSpec<Decimal | null> = {
  descriptor: { kind: "decimal", type: Decimal },
};

/** The JSON text the runtime writes for the row's value. */
export function writtenTextOf(row: DecimalWriteRow): string {
  return JSON.stringify(JsonSerializer.serialize(new Decimal(row.text), decimalSpec));
}

/** The text the runtime must write: STJ's, except for the scale. */
export function expectedTextOf(row: DecimalWriteRow): string {
  return row.metano ?? row.stj;
}

/** The row's value after `serialize` then `deserialize` of the tree, without text. */
export function treeRoundTripOf(row: DecimalWriteRow): Decimal | null {
  return JsonSerializer.deserialize(
    JsonSerializer.serialize(new Decimal(row.text), decimalSpec),
    decimalSpec,
  );
}
