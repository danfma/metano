import { formatBoolean } from "./boolean-format.ts";
import { formatDouble } from "./double-format.ts";
import { NullReferenceException } from "./exceptions/null-reference-exception.ts";
import { boxedText } from "./reflection/boxed-text.ts";
import { isBoxed } from "./reflection/is-boxed.ts";
import type { RuntimeType } from "./reflection/runtime-type.ts";
import { toStringTag } from "./reflection/to-string-tag.ts";
import { formatTimeSpan } from "./time-span-format.ts";
import type { Temporal } from "@js-temporal/polyfill";

/** Default text at an erased object/type-parameter site; null concatenates as empty text. */
export function formatText(value: unknown): string {
  if (value == null) {
    return "";
  }

  if (typeof value === "boolean") {
    return formatBoolean(value);
  }

  if (typeof value === "number") {
    return formatDouble(value);
  }

  if (toStringTag(value) === "[object Temporal.Duration]") {
    return formatTimeSpan(value as Temporal.Duration);
  }

  return String(value);
}

/** An explicit object.ToString() still dereferences its receiver. */
export function objectText(value: unknown): string {
  if (value == null) {
    throw new NullReferenceException();
  }

  return formatText(value);
}

/** Typed enum text uses the same descriptor formatter as a boxed enum. */
export function enumText(value: unknown, type: RuntimeType): string {
  return value == null ? "" : boxedText(isBoxed(value) ? value.value : value, type);
}

/** ValueTuple.ToString(): elements separated by comma-space, enclosed in parentheses. */
export function formatTuple(value: readonly unknown[] | null | undefined): string {
  if (value == null) {
    return "";
  }

  return `(${value.map((item) => (Array.isArray(item) ? formatTuple(item) : formatText(item))).join(", ")})`;
}
