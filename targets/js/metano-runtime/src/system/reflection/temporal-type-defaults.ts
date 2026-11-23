import { Temporal } from "@js-temporal/polyfill";
import type { PrimitiveType } from "./runtime-type.ts";
import { toStringTag } from "./to-string-tag.ts";

const dateTimeZero = (): Temporal.PlainDateTime => new Temporal.PlainDateTime(1, 1, 1);

const dateTimeOffsetZero = (): Temporal.ZonedDateTime =>
  Temporal.ZonedDateTime.from("0001-01-01T00:00:00+00:00[+00:00]");

const dateOnlyZero = (): Temporal.PlainDate => new Temporal.PlainDate(1, 1, 1);

const timeOnlyZero = (): Temporal.PlainTime => new Temporal.PlainTime(0, 0, 0);

const timeSpanZero = (): Temporal.Duration => new Temporal.Duration();

/**
 * Temporal descriptors with `default` and `create` factories, kept in their own
 * module so `castTo`, `isType`, and `getRuntimeType` do not import `@js-temporal/polyfill`.
 */
export const DateTimeDefaultType: PrimitiveType<Temporal.PlainDateTime> = {
  kind: "primitive",
  name: "DateTime",
  fullName: "System.DateTime",
  namespace: "System",
  primitive: "DateTime",
  test: (value: unknown): value is Temporal.PlainDateTime =>
    toStringTag(value) === "[object Temporal.PlainDateTime]",
  default: dateTimeZero,
  create: dateTimeZero,
};

export const DateTimeOffsetDefaultType: PrimitiveType<Temporal.ZonedDateTime> = {
  kind: "primitive",
  name: "DateTimeOffset",
  fullName: "System.DateTimeOffset",
  namespace: "System",
  primitive: "DateTimeOffset",
  test: (value: unknown): value is Temporal.ZonedDateTime =>
    toStringTag(value) === "[object Temporal.ZonedDateTime]",
  default: dateTimeOffsetZero,
  create: dateTimeOffsetZero,
};

export const DateOnlyDefaultType: PrimitiveType<Temporal.PlainDate> = {
  kind: "primitive",
  name: "DateOnly",
  fullName: "System.DateOnly",
  namespace: "System",
  primitive: "DateOnly",
  test: (value: unknown): value is Temporal.PlainDate =>
    toStringTag(value) === "[object Temporal.PlainDate]",
  default: dateOnlyZero,
  create: dateOnlyZero,
};

export const TimeOnlyDefaultType: PrimitiveType<Temporal.PlainTime> = {
  kind: "primitive",
  name: "TimeOnly",
  fullName: "System.TimeOnly",
  namespace: "System",
  primitive: "TimeOnly",
  test: (value: unknown): value is Temporal.PlainTime =>
    toStringTag(value) === "[object Temporal.PlainTime]",
  default: timeOnlyZero,
  create: timeOnlyZero,
};

export const TimeSpanDefaultType: PrimitiveType<Temporal.Duration> = {
  kind: "primitive",
  name: "TimeSpan",
  fullName: "System.TimeSpan",
  namespace: "System",
  primitive: "TimeSpan",
  test: (value: unknown): value is Temporal.Duration =>
    toStringTag(value) === "[object Temporal.Duration]",
  default: timeSpanZero,
  create: timeSpanZero,
};
