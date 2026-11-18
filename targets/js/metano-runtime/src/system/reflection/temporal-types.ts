import type { PrimitiveType } from "./runtime-type.ts";
import { toStringTag } from "./to-string-tag.ts";

// Each type is recognized by the `Symbol.toStringTag` of its Temporal class (the one the
// polyfill and the native API both expose), so `@js-temporal/polyfill` is never imported.

export const DateTimeType: PrimitiveType<object> = {
  kind: "primitive",
  name: "DateTime",
  fullName: "System.DateTime",
  namespace: "System",
  primitive: "DateTime",
  test: (value: unknown): value is object =>
    toStringTag(value) === "[object Temporal.PlainDateTime]",
};

export const DateTimeOffsetType: PrimitiveType<object> = {
  kind: "primitive",
  name: "DateTimeOffset",
  fullName: "System.DateTimeOffset",
  namespace: "System",
  primitive: "DateTimeOffset",
  test: (value: unknown): value is object =>
    toStringTag(value) === "[object Temporal.ZonedDateTime]",
};

export const DateOnlyType: PrimitiveType<object> = {
  kind: "primitive",
  name: "DateOnly",
  fullName: "System.DateOnly",
  namespace: "System",
  primitive: "DateOnly",
  test: (value: unknown): value is object => toStringTag(value) === "[object Temporal.PlainDate]",
};

export const TimeOnlyType: PrimitiveType<object> = {
  kind: "primitive",
  name: "TimeOnly",
  fullName: "System.TimeOnly",
  namespace: "System",
  primitive: "TimeOnly",
  test: (value: unknown): value is object => toStringTag(value) === "[object Temporal.PlainTime]",
};

export const TimeSpanType: PrimitiveType<object> = {
  kind: "primitive",
  name: "TimeSpan",
  fullName: "System.TimeSpan",
  namespace: "System",
  primitive: "TimeSpan",
  test: (value: unknown): value is object => toStringTag(value) === "[object Temporal.Duration]",
};
