import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
import { dayNumber } from "./temporal-helpers.ts";

describe("dayNumber", () => {
  // Reference values from C# DateOnly.DayNumber:
  //   new DateOnly(0001, 01, 01).DayNumber → 0
  //   new DateOnly(0001, 01, 02).DayNumber → 1
  //   new DateOnly(0001, 12, 31).DayNumber → 364
  //   new DateOnly(0002, 01, 01).DayNumber → 365
  //   new DateOnly(1970, 01, 01).DayNumber → 719162
  //   new DateOnly(2000, 01, 01).DayNumber → 730119
  //   new DateOnly(2024, 03, 15).DayNumber → 738959
  //   new DateOnly(2026, 04, 06).DayNumber → 739711
  //   new DateOnly(9999, 12, 31).DayNumber → 3652058

  test("epoch: 0001-01-01 → 0", () => {
    expect(dayNumber(Temporal.PlainDate.from("0001-01-01"))).toBe(0);
  });

  test("0001-01-02 → 1", () => {
    expect(dayNumber(Temporal.PlainDate.from("0001-01-02"))).toBe(1);
  });

  test("end of first year: 0001-12-31 → 364", () => {
    expect(dayNumber(Temporal.PlainDate.from("0001-12-31"))).toBe(364);
  });

  test("start of second year: 0002-01-01 → 365", () => {
    expect(dayNumber(Temporal.PlainDate.from("0002-01-01"))).toBe(365);
  });

  test("unix epoch: 1970-01-01 → 719162", () => {
    expect(dayNumber(Temporal.PlainDate.from("1970-01-01"))).toBe(719162);
  });

  test("Y2K: 2000-01-01 → 730119", () => {
    expect(dayNumber(Temporal.PlainDate.from("2000-01-01"))).toBe(730119);
  });

  test("2024-03-15 → 738959", () => {
    expect(dayNumber(Temporal.PlainDate.from("2024-03-15"))).toBe(738959);
  });

  test("today 2026-04-06 → 739711", () => {
    expect(dayNumber(Temporal.PlainDate.from("2026-04-06"))).toBe(739711);
  });

  test("max: 9999-12-31 → 3652058", () => {
    expect(dayNumber(Temporal.PlainDate.from("9999-12-31"))).toBe(3652058);
  });

  test("subtraction matches duration in days", () => {
    const start = Temporal.PlainDate.from("2026-01-01");
    const end = Temporal.PlainDate.from("2026-01-31");
    expect(dayNumber(end) - dayNumber(start)).toBe(30);
  });

  test("leap year: 2024-02-29 exists and is consistent", () => {
    const feb28 = Temporal.PlainDate.from("2024-02-28");
    const feb29 = Temporal.PlainDate.from("2024-02-29");
    const mar01 = Temporal.PlainDate.from("2024-03-01");
    expect(dayNumber(feb29) - dayNumber(feb28)).toBe(1);
    expect(dayNumber(mar01) - dayNumber(feb29)).toBe(1);
  });

  // ─── PlainDate constructed from components ──────────────

  test("PlainDate from year/month/day components", () => {
    const date = new Temporal.PlainDate(2024, 3, 15);
    expect(dayNumber(date)).toBe(738959);
  });

  test("PlainDate from object fields", () => {
    const date = Temporal.PlainDate.from({ year: 2000, month: 1, day: 1 });
    expect(dayNumber(date)).toBe(730119);
  });

  test("PlainDate from another PlainDate", () => {
    const original = new Temporal.PlainDate(1970, 1, 1);
    const copy = Temporal.PlainDate.from(original);
    expect(dayNumber(copy)).toBe(719162);
  });

  test("PlainDate after arithmetic (.add)", () => {
    const base = new Temporal.PlainDate(2026, 1, 1);
    const later = base.add({ days: 95 }); // 2026-04-06
    expect(dayNumber(later)).toBe(739711);
  });

  test("PlainDate after arithmetic (.subtract)", () => {
    const base = new Temporal.PlainDate(2026, 4, 6);
    const earlier = base.subtract({ days: 95 }); // 2026-01-01
    expect(dayNumber(earlier)).toBe(739616);
  });

  // ─── Fallback: object with toString() ───────────────────

  test("accepts object with toString() returning ISO date", () => {
    const datelike = { toString: () => "2024-03-15" };
    expect(dayNumber(datelike)).toBe(738959);
  });

  test("PlainDateTime extracts date via toString fallback", () => {
    // PlainDateTime.toString() returns "2024-03-15T10:30:00"
    // PlainDate.from() should parse the date portion
    const dt = new Temporal.PlainDateTime(2024, 3, 15, 10, 30, 0);
    expect(dayNumber(dt)).toBe(738959);
  });
});
