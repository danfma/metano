import { describe, expect, test } from "bun:test";
import { durationTicks } from "#/system/temporal-helpers.ts";

import {
  timeSpanFromMinutes,
  timeSpanFromParts,
  timeSpanFromSeconds,
  timeSpanZero,
} from "#/system/time-span.ts";

describe("timeSpanFromParts", () => {
  test("reads hours, minutes and seconds, not years, months and weeks", () => {
    const fiveMinutes = timeSpanFromParts(0, 0, 5, 0);

    expect(fiveMinutes.minutes).toBe(5);
    expect(fiveMinutes.months).toBe(0);
    expect(durationTicks(fiveMinutes)).toBe(3_000_000_000n);
  });

  test("balances the components from days down", () => {
    const value = timeSpanFromParts(1, 25, 61, 61, 1001, 1001);

    expect(value.days).toBe(2);
    expect(value.hours).toBe(2);
    expect(value.minutes).toBe(2);
    expect(value.seconds).toBe(2);
    expect(value.milliseconds).toBe(2);
    expect(value.microseconds).toBe(1);
  });
});

describe("timeSpanFromUnit", () => {
  test("truncates a fraction toward zero", () => {
    expect(durationTicks(timeSpanFromSeconds(0.00000015))).toBe(1n);
    expect(durationTicks(timeSpanFromSeconds(-0.00000015))).toBe(-1n);
  });

  test("takes a bigint exactly", () => {
    expect(durationTicks(timeSpanFromMinutes(2n))).toBe(1_200_000_000n);
  });

  test("zero has no ticks", () => {
    expect(durationTicks(timeSpanZero)).toBe(0n);
  });
});
