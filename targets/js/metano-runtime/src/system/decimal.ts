/**
 * The `decimal` class of generated code: decimal.js configured for `System.Decimal` (#465).
 * decimal.js defaults to 20 significant digits and rounds a midpoint half up; `System.Decimal`
 * holds 28 to 29 significant digits (every integer up to `decimal.MaxValue`, 29 digits, is
 * exact) and rounds a midpoint to the even neighbor. Generated code imports this class where it
 * would import decimal.js's own, through the `[ExportFromBcl]` of `decimal`
 * (`src/Metano/Runtime/Decimal.cs`), so arithmetic never relies on decimal.js's global default.
 *
 * It is a clone, so a `Decimal.set` elsewhere does not reach it, and a value of either class is
 * an `instanceof` the other's (the clones share one prototype). An operation takes its precision
 * from its left operand's class. Division keeps 29 significant digits where `System.Decimal`
 * keeps 28 decimal places (`1m / 3m` has one digit more here), a result below `1e-28` is kept
 * where `System.Decimal` rounds it to zero, and a result's trailing zeros are dropped (`1.10m`
 * prints `1.1`; #662).
 *
 * Not part of the barrel: only a program that uses `decimal` loads decimal.js.
 */

import { Decimal as DecimalJs } from "decimal.js";

/** `System.Decimal`'s 29 significant digits, which keep `decimal.MaxValue` exact. */
const DECIMAL_PRECISION = 29;

export const Decimal = DecimalJs.clone({
  precision: DECIMAL_PRECISION,
  rounding: DecimalJs.ROUND_HALF_EVEN,
});

export type Decimal = DecimalJs;
