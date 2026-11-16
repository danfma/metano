/**
 * The `metano-runtime` functions the conformance rows call, by the name a row uses. Relative
 * imports of the built output (`bun run build` first), because `UUID` is a TypeScript
 * namespace, which Node's type stripping cannot load from source; the same registry then runs
 * under Bun and under Node. A function whose result is a `Temporal` or `UUID` value gets a
 * wrapper that reads it the way the row records it (ticks, UTC ticks and offset, text).
 */

import { tryParseDecimal } from "../../dist/system/decimal-parsing.js";
import * as dateTimeOffsets from "../../dist/system/date-time-offset.js";
import * as stringMembers from "../../dist/system/string-members.js";
import * as timeSpans from "../../dist/system/time-span.js";
import { durationTicks, plainDateTimeTicks } from "../../dist/system/temporal-helpers.js";
import { UUID } from "../../dist/system/uuid.js";
import type { RowRegistry } from "./conformance-rows.ts";
import type { Temporal } from "@js-temporal/polyfill";

export const stringRegistry: RowRegistry = { ...stringMembers };

const ticksOf =
  <TArgs extends never[]>(build: (...args: TArgs) => Temporal.Duration) =>
  (...args: TArgs) =>
    durationTicks(build(...args));

/** The UTC ticks and the offset in minutes of a `DateTimeOffset`, as the rows record it. */
function describeOffset(value: Temporal.ZonedDateTime): [bigint, number] {
  const utc = value.withTimeZone("UTC").toPlainDateTime();

  return [plainDateTimeTicks(utc), value.offsetNanoseconds / 60_000_000_000];
}

const offsetOf = (minutes: number) => timeSpans.timeSpanFromParts(0, 0, minutes, 0);

export const temporalRegistry: RowRegistry = {
  timeSpanFromHoursMinutesSeconds: ticksOf((hours: number, minutes: number, seconds: number) =>
    timeSpans.timeSpanFromParts(0, hours, minutes, seconds),
  ),
  timeSpanFromParts: ticksOf(timeSpans.timeSpanFromParts),
  timeSpanZero: () => durationTicks(timeSpans.timeSpanZero),
  timeSpanFromDays: ticksOf(timeSpans.timeSpanFromDays),
  timeSpanFromHours: ticksOf(timeSpans.timeSpanFromHours),
  timeSpanFromMinutes: ticksOf(timeSpans.timeSpanFromMinutes),
  timeSpanFromSeconds: ticksOf(timeSpans.timeSpanFromSeconds),
  timeSpanFromMilliseconds: ticksOf(timeSpans.timeSpanFromMilliseconds),
  timeSpanFromMicroseconds: ticksOf(timeSpans.timeSpanFromMicroseconds),
  dateTimeOffset: (
    year: number,
    month: number,
    day: number,
    hour: number,
    minute: number,
    second: number,
    millisecond: number,
    offsetMinutes: number,
  ) =>
    describeOffset(
      dateTimeOffsets.dateTimeOffset(
        year,
        month,
        day,
        hour,
        minute,
        second,
        millisecond,
        offsetOf(offsetMinutes),
      ),
    ),
  dateTimeOffsetFromTicks: (ticks: bigint, offsetMinutes: number) =>
    describeOffset(dateTimeOffsets.dateTimeOffsetFromTicks(ticks, offsetOf(offsetMinutes))),
};

export const parsingRegistry: RowRegistry = {
  uuidParse: (text: string | null) => UUID.parse(text),
  tryParseDecimal: (text: string | null) => {
    const [found, value] = tryParseDecimal(text, Decimal);

    return [found, value.toFixed()];
  },
};
