import { describe, expect, test } from "bun:test";

import { Temporal } from "@js-temporal/polyfill";

import { JsonSerializer, type TemporalDescriptor, type TypeSpec } from "#/system/json/index.ts";
import { temporalConverterOf } from "#/system/json/temporal-reads.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";

// Temporal reads as System.Text.Json reads them (#485, measured on .NET 10
// with a source generated context): the inputs it accepts read to the same
// value, every other input throws `JsonException` "The JSON value could not
// be converted to <type>." — a property names the declaring contract, a
// value root names the temporal type — and no Temporal `RangeError` escapes.

// ─── Test Domain ─────────────────────────────────────────────────────────────

class HolderTimeOnly {
  constructor(readonly at: Temporal.PlainTime) {}
}

const timeOnlyDescriptor: TemporalDescriptor = {
  kind: "temporal",
  parse: Temporal.PlainTime.from,
  typeName: "System.TimeOnly",
};

const holderTimeOnlySpec: TypeSpec<HolderTimeOnly> = {
  type: HolderTimeOnly,
  factory: (properties) => new HolderTimeOnly(properties.at as Temporal.PlainTime),
  properties: [{ ts: "at", json: "At", type: timeOnlyDescriptor }],
};

/** The local wall text of an instant, as the reader builds it for an offset. */
function localWallText(iso: string): string {
  const at = new Date(Date.parse(iso));
  const pad = (field: number) => String(field).padStart(2, "0");

  return (
    `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}` +
    `T${pad(at.getHours())}:${pad(at.getMinutes())}:${pad(at.getSeconds())}`
  );
}

/** `±HH:MM`, the offset the system holds at that instant. */
function localOffsetText(iso: string): string {
  const minutes = -new Date(Date.parse(iso)).getTimezoneOffset();
  const sign = minutes < 0 ? "-" : "+";
  const absolute = Math.abs(minutes);

  return `${sign}${String(Math.floor(absolute / 60)).padStart(2, "0")}:${String(absolute % 60).padStart(2, "0")}`;
}

// ─── Accepted inputs ─────────────────────────────────────────────────────────

describe("temporal reads System.Text.Json accepts — TimeOnly", () => {
  const accepted: readonly (readonly [string, string])[] = [
    ["03:04:05", "03:04:05"],
    ["3:04:05", "03:04:05"],
    ["03:04:5", "03:04:05"],
    ["00:00", "00:00:00"],
    ["03:04", "03:04:00"],
    ["0:0:0", "00:00:00"],
    ["003:04:05", "03:04:05"],
    ["0000000000000003:04:05", "03:04:05"],
    ["03:04:05.1", "03:04:05.1"],
    ["03:04:05.1234567", "03:04:05.1234567"],
    ["03:04:05.0000000", "03:04:05"],
    ["23:59:59.9999999", "23:59:59.9999999"],
    ["1:2:3.4", "01:02:03.4"],
  ];

  for (const [input, expected] of accepted) {
    test(`reads ${JSON.stringify(input)}`, () => {
      const time = temporalConverterOf(timeOnlyDescriptor).deserialize(input);

      expect(time).toBeInstanceOf(Temporal.PlainTime);

      expect(String(time)).toBe(expected);
    });
  }
});

describe("temporal reads System.Text.Json accepts — DateOnly", () => {
  const accepted: readonly (readonly [string, string])[] = [
    ["2024-01-31", "2024-01-31"],
    ["0001-01-01", "0001-01-01"],
    ["9999-12-31", "9999-12-31"],
    ["2024-02-29", "2024-02-29"],
  ];

  for (const [input, expected] of accepted) {
    test(`reads ${JSON.stringify(input)}`, () => {
      const date = temporalConverterOf({
        kind: "temporal",
        parse: Temporal.PlainDate.from,
        typeName: "System.DateOnly",
      }).deserialize(input);

      expect(date).toBeInstanceOf(Temporal.PlainDate);

      expect(String(date)).toBe(expected);
    });
  }
});

describe("temporal reads System.Text.Json accepts — DateTime", () => {
  const accepted: readonly (readonly [string, string])[] = [
    ["2024-01-31", "2024-01-31T00:00:00"],
    ["2024-01-31T10:20", "2024-01-31T10:20:00"],
    ["2024-01-31T10:20:30", "2024-01-31T10:20:30"],
    // `Z` keeps the wall time written.
    ["2024-01-31T10:20:30Z", "2024-01-31T10:20:30"],
    ["2024-01-31T10:20:30.1234567", "2024-01-31T10:20:30.1234567"],
    // More digits than a DateTime holds truncate to its 7.
    ["2024-01-31T10:20:30.123456789", "2024-01-31T10:20:30.1234567"],
    ["2024-01-31T10:20:30.5", "2024-01-31T10:20:30.5"],
    // An explicit offset reads the instant as the local wall time.
    ["2024-01-31T10:20:30+02:00", localWallText("2024-01-31T10:20:30+02:00")],
    ["2024-01-31T10:20:30-02:30", localWallText("2024-01-31T10:20:30-02:30")],
    ["2024-01-31T10:20:30-02", localWallText("2024-01-31T10:20:30-02:00")],
  ];

  for (const [input, expected] of accepted) {
    test(`reads ${JSON.stringify(input)}`, () => {
      const instant = temporalConverterOf({
        kind: "temporal",
        parse: Temporal.PlainDateTime.from,
        typeName: "System.DateTime",
      }).deserialize(input);

      expect(instant).toBeInstanceOf(Temporal.PlainDateTime);

      expect(String(instant)).toBe(expected);
    });
  }
});

describe("temporal reads System.Text.Json accepts — DateTimeOffset", () => {
  const readWith = temporalConverterOf({
    kind: "temporal",
    parse: Temporal.ZonedDateTime.from,
    typeName: "System.DateTimeOffset",
  }).deserialize;

  test("keeps an explicit offset", () => {
    const moment = readWith("2024-01-31T10:20:30+02:00");

    expect(String(moment)).toBe("2024-01-31T10:20:30+02:00[+02:00]");
  });

  test("reads `Z` as the `+00:00` offset", () => {
    const moment = readWith("2024-01-31T10:20:30Z");

    expect(String(moment)).toBe("2024-01-31T10:20:30+00:00[+00:00]");
  });

  test("reads a truncated fraction to the 7 a DateTimeOffset holds", () => {
    const moment = readWith("2024-01-31T10:20:30.12345678+02:00");

    expect(String(moment)).toBe("2024-01-31T10:20:30.1234567+02:00[+02:00]");
  });

  test("assumes the local offset without one", () => {
    const moment = readWith("2024-01-31T10:20:30");

    expect(String(moment)).toBe(
      `2024-01-31T10:20:30${localOffsetText("2024-01-31T10:20:30")}[${localOffsetText("2024-01-31T10:20:30")}]`,
    );
  });

  test("reads the zone-suffixed text this runtime writes (DECIDED-16)", () => {
    expect(String(readWith("2024-01-31T10:20:30[Europe/Paris]"))).toBe(
      "2024-01-31T10:20:30+01:00[Europe/Paris]",
    );

    expect(String(readWith("2024-01-31T10:20:30+00:00[UTC]"))).toBe(
      "2024-01-31T10:20:30+00:00[UTC]",
    );
  });
});

describe("temporal reads System.Text.Json accepts — TimeSpan", () => {
  const accepted: readonly (readonly [string, string])[] = [
    ["10:20", "PT10H20M"],
    ["10:20:30", "PT10H20M30S"],
    ["10", "P10D"],
    ["0", "PT0S"],
    ["-0", "PT0S"],
    ["-10:20:30", "-PT10H20M30S"],
    ["1:2:3", "PT1H2M3S"],
    ["1.10:20:30", "P1DT10H20M30S"],
    ["-1.10:20", "-P1DT10H20M"],
    ["10:20:30.1234567", "PT10H20M30.1234567S"],
    ["10675199.02:48:05.4775807", "P10675199DT2H48M5.4775807S"],
    ["-10675199.02:48:05.4775808", "-P10675199DT2H48M5.4775808S"],
    // The ISO duration this runtime writes (DECIDED-16) reads too.
    ["PT1H2M3S", "PT1H2M3S"],
  ];

  for (const [input, expected] of accepted) {
    test(`reads ${JSON.stringify(input)}`, () => {
      const duration = temporalConverterOf({
        kind: "temporal",
        parse: Temporal.Duration.from,
        typeName: "System.TimeSpan",
      }).deserialize(input);

      expect(duration).toBeInstanceOf(Temporal.Duration);

      expect(String(duration)).toBe(expected);
    });
  }
});

// ─── Rejected inputs ─────────────────────────────────────────────────────────

describe("temporal reads System.Text.Json rejects", () => {
  const rejected: readonly (readonly [TemporalDescriptor, string])[] = [
    [timeOnlyDescriptor, "03:04:60"],
    [timeOnlyDescriptor, "T03:04:05"],
    [timeOnlyDescriptor, "03:04:05.12345678"],
    [timeOnlyDescriptor, "03:04:05.123456789"],
    [timeOnlyDescriptor, "24:00:00"],
    [timeOnlyDescriptor, "99:99:99"],
    [timeOnlyDescriptor, "03:04:05+01:00"],
    [timeOnlyDescriptor, "03:04:05Z"],
    [timeOnlyDescriptor, "03:04:05,123"],
    [timeOnlyDescriptor, " 03:04:05"],
    [timeOnlyDescriptor, "03:04:05 "],
    [timeOnlyDescriptor, "13:04:05 PM"],
    [timeOnlyDescriptor, ""],
    [
      { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      "2024-1-31",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      "20240131",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      "2024-01-31T00:00:00",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      "2024-02-30",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      "2023-02-29",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      "0000-01-01",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDate.from, typeName: "System.DateOnly" },
      "2024-01-31 ",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-1-31",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T1:20:30",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T10",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T10:20.5",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31 10:20:30",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T24:00:00",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T10:20:60",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-13-01T00:00:00",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31t10:20:30z",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T10:20:30+2:00",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T10:20:30+0200",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T10:20:30+02:00:30",
    ],
    [
      { kind: "temporal", parse: Temporal.PlainDateTime.from, typeName: "System.DateTime" },
      "2024-01-31T10:20:30 UTC",
    ],
    [
      { kind: "temporal", parse: Temporal.ZonedDateTime.from, typeName: "System.DateTimeOffset" },
      "2024-01-31T10:20:30+0200",
    ],
    [
      { kind: "temporal", parse: Temporal.ZonedDateTime.from, typeName: "System.DateTimeOffset" },
      "2024-01-31t10:20:30z",
    ],
    [
      { kind: "temporal", parse: Temporal.ZonedDateTime.from, typeName: "System.DateTimeOffset" },
      "2024-01-31T10:20:30[Not/AZone]",
    ],
    [
      { kind: "temporal", parse: Temporal.ZonedDateTime.from, typeName: "System.DateTimeOffset" },
      "2024-01-31T1:20:30",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "10:20:30:40",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "+10:20:30",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "10:20:30.12345678",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "10:20:30Z",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "10:60:30",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "24:00:00",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "1.10",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "10:20.5",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      ".5",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      "10675200.00:00:00",
    ],
    [
      { kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" },
      " 10:20:30",
    ],
    [{ kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" }, ""],
    [{ kind: "temporal", parse: Temporal.Duration.from, typeName: "System.TimeSpan" }, "PXY"],
  ];

  for (const [descriptor, input] of rejected) {
    test(`${descriptor.typeName} rejects ${JSON.stringify(input)}`, () => {
      expectJsonException(
        () => temporalConverterOf(descriptor).deserialize(input),
        cannotConvertMessage(descriptor.typeName!),
      );
    });
  }
});

// ─── Positions and kinds ─────────────────────────────────────────────────────

describe("temporal read errors name the type of their position", () => {
  test("a property names the declaring contract", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ At: "03:04:60" }, holderTimeOnlySpec),
      cannotConvertMessage("HolderTimeOnly"),
    );
  });

  test("a property names the contract for a non-string value too", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ At: 5 }, holderTimeOnlySpec),
      cannotConvertMessage("HolderTimeOnly"),
    );
  });

  test("a value root names the temporal type for a non-string value", () => {
    expectJsonException(
      () => temporalConverterOf(timeOnlyDescriptor).deserialize(5),
      cannotConvertMessage("System.TimeOnly"),
    );
  });
});

describe("a hand-written temporal descriptor parses, with no RangeError out", () => {
  const parseOnly: TemporalDescriptor = { kind: "temporal", parse: Temporal.PlainTime.from };

  test("reads what Temporal reads, lenient as Temporal is", () => {
    expect(String(temporalConverterOf(parseOnly).deserialize("03:04:60"))).toBe("03:04:59");
  });

  test("reports a Temporal rejection as the conversion error", () => {
    expectJsonException(
      () => temporalConverterOf(parseOnly).deserialize("24:00:00"),
      cannotConvertMessage("Object"),
    );
  });
});
