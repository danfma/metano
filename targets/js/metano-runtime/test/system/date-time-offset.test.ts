import { describe, expect, test } from "bun:test";
import { dateTimeOffset, dateTimeOffsetFromDateTime } from "#/system/date-time-offset.ts";
import { ArgumentException, ArgumentOutOfRangeException } from "#/system/exceptions/index.ts";
import { plainDateTime } from "#/system/temporal-helpers.ts";
import { timeSpanFromParts } from "#/system/time-span.ts";

const thirtySeconds = timeSpanFromParts(0, 0, 0, 30);
const nineMinutes = timeSpanFromParts(0, 0, 9, 0);

describe("dateTimeOffset", () => {
  test("keeps the clock time at the offset", () => {
    const value = dateTimeOffset(2024, 1, 2, 3, 4, 5, 678, timeSpanFromParts(0, 5, 30, 0));

    expect(value.toString()).toBe("2024-01-02T03:04:05.678+05:30[+05:30]");
    expect(value.epochMilliseconds).toBe(Date.UTC(2024, 0, 1, 21, 34, 5, 678));
  });

  test("rejects an offset that is not a whole number of minutes", () => {
    expect(() => dateTimeOffset(2024, 1, 2, 3, 4, 5, 0, thirtySeconds)).toThrow(ArgumentException);
  });

  test("rejects an offset beyond fourteen hours", () => {
    expect(() => dateTimeOffset(2024, 1, 2, 3, 4, 5, 0, timeSpanFromParts(0, 14, 1, 0))).toThrow(
      ArgumentOutOfRangeException,
    );
  });

  test("builds from a DateTime and an offset", () => {
    const clock = plainDateTime(2024, 1, 2, 3, 4, 5);

    expect(dateTimeOffsetFromDateTime(clock, nineMinutes).toString()).toBe(
      "2024-01-02T03:04:05+00:09[+00:09]",
    );
  });
});
