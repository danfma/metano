/**
 * The TypeScript twin of `tests/Int64Reference/RuntimeFormatting.cs`
 * (baseline FR-051): the runtime formatters called with a format operand, as
 * `ToString(format)` is on .NET. Hand-written because a transpiled call with
 * a format that is not a constant reports MS0001 and throws.
 */

import { formatDouble, formatInteger } from "metano-runtime";

export const runtimeFormatters = {
  formatDouble: (formatted: number, numberFormat: string): string =>
    formatDouble(formatted, numberFormat),
  formatInt32: (formatted: number, numberFormat: string): string =>
    formatInteger(formatted, 32, numberFormat),
  formatUInt32: (formatted: number, numberFormat: string): string =>
    formatInteger(formatted, 32, numberFormat),
  formatInt16: (formatted: number, numberFormat: string): string =>
    formatInteger(formatted, 16, numberFormat),
  formatUInt16: (formatted: number, numberFormat: string): string =>
    formatInteger(formatted, 16, numberFormat),
  formatSByte: (formatted: number, numberFormat: string): string =>
    formatInteger(formatted, 8, numberFormat),
  formatByte: (formatted: number, numberFormat: string): string =>
    formatInteger(formatted, 8, numberFormat),
};
