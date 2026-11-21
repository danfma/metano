import { Temporal } from "@js-temporal/polyfill";
import type { PrimitiveType } from "./runtime-type.ts";
import { toStringTag } from "./to-string-tag.ts";

// Each type is recognized by the `Symbol.toStringTag` of its Temporal class (the one the
// polyfill and the native API both expose). `default`/`create` return the .NET zero value.

export const DateTimeType: PrimitiveType<Temporal.PlainDateTime> = {
  kind: "primitive",
  name: "DateTime",
  fullName: "System.DateTime",
  namespace: "System",
  primitive: "DateTime",
  test: (value: unknown): value is Temporal.PlainDateTime =>
    toStringTag(value) === "[object Temporal.PlainDateTime]",
  default: () => new Temporal.PlainDateTime(1, 1, 1),
  create: () => new Temporal.PlainDateTime(1, 1, 1),
};

export const DateTimeOffsetType: PrimitiveType<Temporal.ZonedDateTime> = {
  kind: "primitive",
  name: "DateTimeOffset",
  fullName: "System.DateTimeOffset",
  namespace: "System",
  primitive: "DateTimeOffset",
  test: (value: unknown): value is Temporal.ZonedDateTime =>
    toStringTag(value) === "[object Temporal.ZonedDateTime]",
  default: () => Temporal.ZonedDateTime.from("0001-01-01T00:00:00+00:00[+00:00]"),
  create: () => Temporal.ZonedDateTime.from("0001-01-01T00:00:00+00:00[+00:00]"),
};

export const DateOnlyType: PrimitiveType<Temporal.PlainDate> = {
  kind: "primitive",
  name: "DateOnly",
  fullName: "System.DateOnly",
  namespace: "System",
  primitive: "DateOnly",
  test: (value: unknown): value is Temporal.PlainDate =>
    toStringTag(value) === "[object Temporal.PlainDate]",
  default: () => new Temporal.PlainDate(1, 1, 1),
  create: () => new Temporal.PlainDate(1, 1, 1),
};

export const TimeOnlyType: PrimitiveType<Temporal.PlainTime> = {
  kind: "primitive",
  name: "TimeOnly",
  fullName: "System.TimeOnly",
  namespace: "System",
  primitive: "TimeOnly",
  test: (value: unknown): value is Temporal.PlainTime =>
    toStringTag(value) === "[object Temporal.PlainTime]",
  default: () => new Temporal.PlainTime(0, 0, 0),
  create: () => new Temporal.PlainTime(0, 0, 0),
};

export const TimeSpanType: PrimitiveType<Temporal.Duration> = {
  kind: "primitive",
  name: "TimeSpan",
  fullName: "System.TimeSpan",
  namespace: "System",
  primitive: "TimeSpan",
  test: (value: unknown): value is Temporal.Duration =>
    toStringTag(value) === "[object Temporal.Duration]",
  default: () => new Temporal.Duration(),
  create: () => new Temporal.Duration(),
};
