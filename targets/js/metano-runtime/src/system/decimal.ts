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
 * where `System.Decimal` rounds it to zero. Arithmetic still drops scale (#662);
 * constructed values retain their input scale for default text.
 *
 * Not part of the barrel: only a program that uses `decimal` loads decimal.js.
 */

import { Decimal as DecimalJs } from "decimal.js";

/** `System.Decimal`'s 29 significant digits, which keep `decimal.MaxValue` exact. */
const DECIMAL_PRECISION = 29;

/**
 * decimal.js's `ROUND_HALF_EVEN`, written as its value: reading `DecimalJs.ROUND_HALF_EVEN`
 * here would be a property read at load, which keeps the clone in every bundle.
 */
const ROUND_HALF_EVEN = 6;

// Pure (spec 011 FR-043): a side-effect-only import of this module bundles to nothing.
const DecimalNumber = /* @__PURE__ */ DecimalJs.clone({
  precision: DECIMAL_PRECISION,
  rounding: ROUND_HALF_EVEN,
});

const textScales = /* @__PURE__ */ new WeakMap<DecimalJs, number>();

/** Retains constructor scale without changing decimal.js's prototype or static configuration. */
export const Decimal = /* @__PURE__ */ withDefaultTextScale(DecimalNumber);
export type Decimal = DecimalJs;

function withDefaultTextScale(decimalConstructor: typeof DecimalJs): typeof DecimalJs {
  return new Proxy(decimalConstructor, {
    construct(target, argumentsList: readonly unknown[]) {
      return decimalWithScale(target, argumentsList[0] as DecimalJs.Value);
    },
    apply(target, _receiver: unknown, argumentsList: readonly unknown[]) {
      return decimalWithScale(target, argumentsList[0] as DecimalJs.Value);
    },
  });
}

function decimalWithScale(
  decimalConstructor: typeof DecimalJs,
  source: DecimalJs.Value,
): DecimalJs {
  const value = new decimalConstructor(source);
  textScales.set(value, scaleOf(source, value.decimalPlaces()));

  Object.defineProperties(value, {
    toString: { value: decimalText, writable: true, configurable: true },
    valueOf: { value: decimalText, writable: true, configurable: true },
  });

  return value;
}

function decimalText(this: DecimalJs): string {
  return this.toFixed(Math.max(textScales.get(this) ?? 0, this.decimalPlaces()));
}

function scaleOf(value: DecimalJs.Value, fallback: number): number {
  if (typeof value !== "string") {
    return typeof value === "object" ? (textScales.get(value) ?? fallback) : fallback;
  }

  const [mantissa = "", exponent = "0"] = value.toLowerCase().split("e");
  const fraction = mantissa.includes(".") ? mantissa.length - mantissa.indexOf(".") - 1 : 0;

  return Math.max(0, fraction - Number(exponent));
}
