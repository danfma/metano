import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
import { ArgumentOutOfRangeException } from "#/system/exceptions/index.ts";
import {
  durationFromTicks,
  durationTicks,
  plainDateTimeFromTicks,
  plainDateTimeTicks,
} from "#/system/temporal-helpers.ts";

/** Every expected value below was printed by the same `TimeSpan` / `DateTime` call on .NET 10.0.12. */
describe("ticks", () => {
  test("durationTicks counts 100 ns ticks", () => {
    expect(durationTicks(Temporal.Duration.from({ seconds: 1 }))).toBe(10000000n);
    expect(
      durationTicks(Temporal.Duration.from({ days: 1, hours: 2, minutes: 3, seconds: 4, milliseconds: 5 })),
    ).toBe(937840050000n);
    expect(durationTicks(Temporal.Duration.from({ nanoseconds: -1500 }))).toBe(-15n);
  });

  test("durationFromTicks balances days down to nanoseconds", () => {
    expect(durationFromTicks(15n).toString()).toBe("PT0.0000015S");
    expect(durationFromTicks(-15n).toString()).toBe("-PT0.0000015S");
    expect(durationTicks(durationFromTicks(9223372036854775807n))).toBe(9223372036854775807n);
    expect(durationFromTicks(9223372036854775807n).days).toBe(10675199);
    expect(durationTicks(durationFromTicks(-9223372036854775808n))).toBe(-9223372036854775808n);
  });

  test("plainDateTimeTicks counts from 0001-01-01T00:00:00", () => {
    expect(plainDateTimeTicks(Temporal.PlainDateTime.from("2026-01-01T00:00"))).toBe(639028224000000000n);
    expect(plainDateTimeTicks(Temporal.PlainDateTime.from("2026-01-01T13:14:15.016"))).toBe(
      639028700550160000n,
    );
    expect(plainDateTimeTicks(Temporal.PlainDateTime.from("9999-12-31T23:59:59.9999999"))).toBe(
      3155378975999999999n,
    );
  });

  test("plainDateTimeFromTicks rebuilds the date and time and rejects ticks outside DateTime", () => {
    expect(plainDateTimeFromTicks(639028224000000123n).toString()).toBe("2026-01-01T00:00:00.0000123");
    expect(plainDateTimeFromTicks(0n).toString()).toBe("0001-01-01T00:00:00");
    expect(() => plainDateTimeFromTicks(-1n)).toThrow(
      new ArgumentOutOfRangeException(
        "ticks",
        null,
        "Ticks must be between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks.",
      ),
    );
    expect(() => plainDateTimeFromTicks(3155378976000000000n)).toThrow(ArgumentOutOfRangeException);
  });
});
