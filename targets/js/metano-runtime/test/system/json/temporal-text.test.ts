import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
import { JsonSerializer, type ValueSpec } from "#/system/json/index.ts";
import { temporalTextOf } from "#/system/json/temporal-text.ts";

// System.Text.Json writes TimeOnly in the TimeSpan "c" form (measured on
// .NET 10): no fraction when it is zero, otherwise exactly seven digits.
const timeOnlySpec: ValueSpec<Temporal.PlainTime> = {
  descriptor: { kind: "temporal", parse: Temporal.PlainTime.from },
};

describe("temporalTextOf — TimeOnly (Temporal.PlainTime)", () => {
  test.each([
    ["03:04:05", "03:04:05"],
    ["03:04:05.678", "03:04:05.6780000"],
    ["03:04:05.1", "03:04:05.1000000"],
    ["03:04:05.0000001", "03:04:05.0000001"],
    ["03:04:05.1234567", "03:04:05.1234567"],
    ["00:00:00", "00:00:00"],
    ["23:59:59.999999", "23:59:59.9999990"],
  ])("%s writes as STJ does (%s)", (input, expected) => {
    expect(temporalTextOf(Temporal.PlainTime.from(input))).toBe(expected);
  });

  test("truncates a fraction to ticks (100 ns)", () => {
    expect(temporalTextOf(Temporal.PlainTime.from("03:04:05.123456789"))).toBe(
      "03:04:05.1234567",
    );
  });

  test("a fraction below one tick writes no fraction", () => {
    expect(temporalTextOf(Temporal.PlainTime.from("03:04:05.000000099"))).toBe("03:04:05");
  });

  const native = (globalThis as { Temporal?: typeof Temporal }).Temporal;

  test.skipIf(native === undefined)("recognizes the native Temporal.PlainTime too", () => {
    expect(temporalTextOf(native!.PlainTime.from("03:04:05.678"))).toBe("03:04:05.6780000");
  });
});

describe("temporalTextOf — other temporal kinds keep toString()", () => {
  test.each([
    ["2026-01-02T03:04:05.678", "2026-01-02T03:04:05.678"],
    ["2026-01-02T03:04:05.100", "2026-01-02T03:04:05.1"],
    ["2026-01-02T03:04:05", "2026-01-02T03:04:05"],
    ["2026-01-02T03:04:05.1234567", "2026-01-02T03:04:05.1234567"],
  ])("DateTime %s trims trailing fraction zeros, as STJ does (%s)", (input, expected) => {
    expect(temporalTextOf(Temporal.PlainDateTime.from(input))).toBe(expected);
  });

  test("DateOnly", () => {
    expect(temporalTextOf(Temporal.PlainDate.from("2026-01-02"))).toBe("2026-01-02");
  });
});

describe("JsonSerializer — TimeOnly value root", () => {
  test("serializes in the STJ form", () => {
    const time = Temporal.PlainTime.from("03:04:05.678");

    expect(JsonSerializer.serialize(time, timeOnlySpec)).toBe("03:04:05.6780000");
  });

  test.each([
    ["03:04:05.6780000", "03:04:05.678"],
    ["03:04:05.678", "03:04:05.678"],
    ["03:04:05", "03:04:05"],
    ["03:04", "03:04:00"],
  ])("reads the STJ text %s", (text, expected) => {
    const time = JsonSerializer.deserialize(text, timeOnlySpec);

    expect(time).toBeInstanceOf(Temporal.PlainTime);
    expect(time?.equals(Temporal.PlainTime.from(expected))).toBe(true);
  });

  test("roundtrips the written text", () => {
    const time = Temporal.PlainTime.from("03:04:05.678");
    const text = JsonSerializer.serialize(time, timeOnlySpec) as string;

    expect(JsonSerializer.deserialize(text, timeOnlySpec)?.equals(time)).toBe(true);
  });
});
