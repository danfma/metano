import { describe, expect, test } from "bun:test";

import { IssueActivityLog } from "#/sample-issue-tracker/issues/application";
import {
  IssueActivity,
  IssueActivityKind,
} from "#/sample-issue-tracker/issues/domain";
import { SnowflakeId } from "#/sample-issue-tracker/shared-kernel";

// The ids and ticks of the 64-bit scenario (spec 012 FR-068): everything
// here lives beyond 2^53, where a JS number would already lose digits.
const FIRST_ID = SnowflakeId.create(1152921504606846977n);
const SECOND_ID = SnowflakeId.create(1152921504606846978n);
const SNOWFLAKE_TICKS = 639028224000000000n;

function makeActivity(
  id: SnowflakeId,
  overrides: {
    issueKey?: string;
    kind?: IssueActivityKind;
    occurredAtTicks?: bigint;
    durationTicks?: bigint | null;
  } = {},
): IssueActivity {
  return new IssueActivity(
    id,
    overrides.issueKey ?? "ISSUE-1",
    overrides.kind ?? IssueActivityKind.Created,
    overrides.occurredAtTicks ?? SNOWFLAKE_TICKS,
    "durationTicks" in overrides ? (overrides.durationTicks ?? null) : 9007199254740993n,
  );
}

describe("IssueActivityLog — 64-bit ids as dictionary keys", () => {
  const log = new IssueActivityLog();

  log.record(makeActivity(FIRST_ID));
  log.record(makeActivity(SECOND_ID, { issueKey: "ISSUE-2" }));

  test("finds an entry by its bigint id above 2^53", () => {
    const found = log.find(1152921504606846977n);

    expect(found).not.toBeNull();
    expect(found!.id).toBe(FIRST_ID);
    expect(found!.issueKey).toBe("ISSUE-1");
  });

  test("keeps bigint dictionary keys, never numbers", () => {
    for (const key of log.activities.keys()) {
      expect(typeof key).toBe("bigint");
    }

    expect([...log.activities.keys()]).toEqual([
      1152921504606846977n,
      1152921504606846978n,
    ]);
  });

  test("a missing id answers null, not a rounded number", () => {
    expect(log.find(9007199254740993n)).toBeNull();
  });

  test("remove drops the entry by its bigint id", () => {
    const log = new IssueActivityLog();

    log.record(makeActivity(FIRST_ID));

    expect(log.remove(1152921504606846977n)).toBe(true);
    expect(log.count).toBe(0);
  });
});

describe("IssueActivityLog — tick math on timestamps", () => {
  test("elapsed ticks wrap where C# wraps", () => {
    const activity = makeActivity(FIRST_ID, {
      occurredAtTicks: -9223372036854775807n,
    });

    // 7 - (-9223372036854775807) overflows long; the wrap is what C#
    // computes unchecked.
    expect(activity.elapsedTicksUntil(7n)).toBe(-9223372036854775802n);
  });

  test("occurredWithin answers the interval bounds inclusively", () => {
    const activity = makeActivity(FIRST_ID);

    expect(activity.occurredWithin(10n, SNOWFLAKE_TICKS)).toBe(true);
    expect(activity.occurredWithin(0n, SNOWFLAKE_TICKS - 1n)).toBe(false);
  });

  test("the tick-based duration round-trips through TimeSpan ticks", () => {
    const activity = makeActivity(FIRST_ID);

    expect(activity.durationTicks).toBe(9007199254740993n);
  });

  test("totalDurationTicks sums the checked 64-bit way", () => {
    const log = new IssueActivityLog();

    log.record(makeActivity(FIRST_ID, { durationTicks: 9007199254740993n }));
    log.record(makeActivity(SECOND_ID, { durationTicks: 7n }));

    expect(log.totalDurationTicks()).toBe(9007199254741000n);
  });

  test("totalDurationTicks throws past long.MaxValue, as the checked Sum does", () => {
    const log = new IssueActivityLog();

    log.record(makeActivity(FIRST_ID, { durationTicks: 9223372036854775807n }));
    log.record(makeActivity(SECOND_ID, { durationTicks: 1n }));

    expect(() => log.totalDurationTicks()).toThrow(/resulted in an overflow/);
  });

  test("within orders the entries by their tick timestamps", () => {
    const log = new IssueActivityLog();

    log.record(makeActivity(SECOND_ID, { occurredAtTicks: 20n }));
    log.record(makeActivity(FIRST_ID, { occurredAtTicks: 10n }));

    const within = log.within(0n, 100n);

    expect(within.map((activity) => activity.occurredAtTicks)).toEqual([10n, 20n]);
  });
});

describe("SnowflakeId — the 64-bit id itself", () => {
  test("the validating factory rejects zero and negatives", () => {
    expect(() => SnowflakeId.from(0n)).toThrow(/must be positive/);
    expect(() => SnowflakeId.from(-1n)).toThrow(/must be positive/);
  });

  test("reads the timestamp bits with an unsigned shift", () => {
    const id = SnowflakeId.create(17592186044416n);

    expect(SnowflakeId.timestampMilliseconds(id)).toBe(4194304n);
  });

  test("two ids that differ only in the high word stay distinct", () => {
    const low = SnowflakeId.create(1n);
    const high = SnowflakeId.create(4294967296n);

    expect(low === high).toBe(false);
    expect(SnowflakeId.timestampMilliseconds(low)).not.toBe(
      SnowflakeId.timestampMilliseconds(high),
    );
  });
});
