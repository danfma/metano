import { expect, test } from "bun:test";
import { formatText } from "#/system/default-text.ts";
import { durationFromTicks } from "#/system/temporal-helpers.ts";
import { formatTimeSpan } from "#/system/time-span-format.ts";

test("TimeSpan constant text keeps sign, days and seven tick digits", () => {
  const rows = [
    [0n, "00:00:00"],
    [1n, "00:00:00.0000001"],
    [37230000000n, "01:02:03"],
    [15000000n, "00:00:01.5000000"],
    [-864000000001n, "-1.00:00:00.0000001"],
    [9223372036854775807n, "10675199.02:48:05.4775807"],
    [-9223372036854775808n, "-10675199.02:48:05.4775808"],
  ] as const;

  for (const [ticks, text] of rows) {
    const duration = durationFromTicks(ticks);
    expect(formatTimeSpan(duration)).toBe(text);
    expect(formatText(duration)).toBe(text);
  }

  expect(formatTimeSpan(null)).toBe("");
});
