import { describe, expect, test } from "bun:test";
import { JsonSerializer } from "metano-runtime/system/json";

import { ActivityFeed, IssueActivity, IssueActivityKind } from "#/sample-issue-tracker/issues/domain";
import { JsonContext } from "#/sample-issue-tracker/json-context";
import { SnowflakeId } from "#/sample-issue-tracker/shared-kernel";

// The JSON half of the 64-bit scenario (spec 012 FR-065, FR-068): ids and
// ticks beyond 2^53 round-trip through the generated context with every
// digit, as System.Text.Json writes them.
const ctx = JsonContext.default;

const ID = SnowflakeId.create(1152921504606846977n);

const activity = new IssueActivity(
  ID,
  "ISSUE-1",
  IssueActivityKind.Created,
  639028224000000000n,
  9007199254740993n,
);

const activityText =
  '{"id":1152921504606846977,"issueKey":"ISSUE-1","kind":"created",' +
  '"occurredAtTicks":639028224000000000,"durationTicks":9007199254740993}';

describe("int64 JSON — the issue activity scenario", () => {
  test("writes the exact digits, unquoted, like System.Text.Json", () => {
    expect(JSON.stringify(JsonSerializer.serialize(activity, ctx.issueActivity))).toBe(
      activityText,
    );
  });

  test("reads a .NET-written payload back into exact bigints", () => {
    const read = JsonSerializer.deserializeText(activityText, ctx.issueActivity)!;

    expect(read.id).toBe(ID);
    expect(read.occurredAtTicks).toBe(639028224000000000n);
    expect(read.durationTicks).toBe(9007199254740993n);
  });

  test("a JSON.parse'd payload beyond 2^53 throws, pointing at the exact path", () => {
    // JSON.parse already rounded the id to 1152921504606846976; the tree
    // lost the digits before the runtime saw them (spec 012 OQ-3 (d)).
    expect(() =>
      JsonSerializer.deserialize(JSON.parse(activityText), ctx.issueActivity),
    ).toThrow(/deserializeText/);
  });

  test("round-trips byte-identically through the tree", () => {
    const json = JsonSerializer.serialize(activity, ctx.issueActivity);
    const read = JsonSerializer.deserialize(json, ctx.issueActivity)!;

    expect(JSON.stringify(JsonSerializer.serialize(read, ctx.issueActivity))).toBe(activityText);
  });

  test("round-trips long.MinValue ticks through the tree", () => {
    const extreme = new IssueActivity(
      ID,
      "ISSUE-2",
      IssueActivityKind.Closed,
      -9223372036854775808n,
      null,
    );

    const json = JsonSerializer.serialize(extreme, ctx.issueActivity);

    expect(JSON.stringify(json)).toContain("-9223372036854775808");

    const read = JsonSerializer.deserialize(json, ctx.issueActivity)!;

    expect(read.occurredAtTicks).toBe(-9223372036854775808n);
    expect(read.durationTicks).toBeNull();
  });

  test("the serialized tree holds rawJSON leaves for the 64-bit values", () => {
    const json = JsonSerializer.serialize(activity, ctx.issueActivity);

    // TypeScript's lib does not declare JSON.rawJSON / JSON.isRawJSON yet
    // (the same cast docs/serialization.md teaches).
    const leaf = json.id as { readonly rawJSON: string };

    expect(typeof (JSON as { isRawJSON?: unknown }).isRawJSON).toBe("function");
    expect(leaf.rawJSON).toBe("1152921504606846977");
  });

  test("the tree passes serialize → deserialize without a stringify trip", () => {
    const read = JsonSerializer.deserialize(
      JsonSerializer.serialize(activity, ctx.issueActivity),
      ctx.issueActivity,
    )!;

    expect(read.occurredAtTicks).toBe(639028224000000000n);
    expect(read.id).toBe(ID);
  });

  test("JSON.stringify on the raw record still throws — the context is the writer", () => {
    expect(() => JSON.stringify(activity)).toThrow(/BigInt/);
  });
});

describe("int64 JSON — the snowflake-keyed activity feed", () => {
  const feed = new ActivityFeed(
    new Map<bigint, IssueActivity>([
      [1152921504606846977n, activity],
      [
        1152921504606846978n,
        new IssueActivity(
          SnowflakeId.create(1152921504606846978n),
          "ISSUE-2",
          IssueActivityKind.Assigned,
          639028224000000001n,
          null,
        ),
      ],
    ]),
  );

  test("writes the bigint keys as their digits, as STJ writes the names", () => {
    const text = JSON.stringify(JsonSerializer.serialize(feed, ctx.activityFeed));

    expect(text).toContain('"1152921504606846977":{');
    expect(text).toContain('"1152921504606846978":{');
  });

  test("reads the keys back as bigints, never rounded numbers", () => {
    const json = JsonSerializer.serialize(feed, ctx.activityFeed);
    const read = JsonSerializer.deserialize(json, ctx.activityFeed)!;

    expect(read.entries).toBeInstanceOf(Map);

    for (const key of read.entries.keys()) {
      expect(typeof key).toBe("bigint");
    }

    expect(read.entries.get(1152921504606846977n)!.occurredAtTicks).toBe(639028224000000000n);
    expect(read.entries.get(1152921504606846978n)!.id).toBe(
      SnowflakeId.create(1152921504606846978n),
    );
  });

  test("an activity array root keeps the digits of every entry", () => {
    const json = JsonSerializer.serialize([activity], ctx.issueActivityArray);
    const text = JSON.stringify(json);

    expect(text).toContain("1152921504606846977");

    const read = JsonSerializer.deserialize(json, ctx.issueActivityArray)!;

    expect(read[0]!.id).toBe(ID);
  });
});

// The exact text path (spec 012 FR-053, slice 012h): `deserializeText` hands
// every 64-bit leaf that is not a short integer token its source text, so
// the generated converters read digits beyond 2^53 and reject what
// System.Text.Json rejects (`1.0`, `1e3`), in every position.
describe("int64 JSON — exact reads from text", () => {
  const activityJson = (fields: string) =>
    `{"id":1152921504606846977,"issueKey":"ISSUE-1","kind":"created",${fields}}`;

  test("a snowflake-keyed feed reads every key and value from its digits", () => {
    const feed = JsonSerializer.deserializeText(
      `{"entries":{"1152921504606846977":${activityText},` +
        `"-9223372036854775808":${activityJson('"occurredAtTicks":9223372036854775807,"durationTicks":null')}}}`,
      ctx.activityFeed,
    )!;

    expect(feed.entries.get(1152921504606846977n)!.durationTicks).toBe(9007199254740993n);
    expect(feed.entries.get(-9223372036854775808n)!.occurredAtTicks).toBe(9223372036854775807n);
  });

  test("an activity array root reads its entries' digits", () => {
    const read = JsonSerializer.deserializeText(`[${activityText}]`, ctx.issueActivityArray)!;

    expect(read[0]!.id).toBe(ID);
    expect(read[0]!.occurredAtTicks).toBe(639028224000000000n);
  });

  test("a short fraction or exponent throws, as System.Text.Json throws", () => {
    const rejected = [
      '{"id":1.0,"issueKey":"ISSUE-1","kind":"created","occurredAtTicks":1,"durationTicks":null}',
      activityJson('"occurredAtTicks":1e3,"durationTicks":null'),
    ];

    for (const text of rejected) {
      expect(() => JsonSerializer.deserializeText(text, ctx.issueActivity)).toThrow(
        "The JSON value could not be converted to IssueActivity.",
      );
    }

    // A nullable member names the nullable (spec 009's nullable-inner design).
    expect(() =>
      JsonSerializer.deserializeText(
        activityJson('"occurredAtTicks":1,"durationTicks":2.0'),
        ctx.issueActivity,
      ),
    ).toThrow("The JSON value could not be converted to System.Nullable`1[System.Int64].");

    expect(() =>
      JsonSerializer.deserializeText(
        `[${activityJson('"occurredAtTicks":1.5,"durationTicks":null')}]`,
        ctx.issueActivityArray,
      ),
    ).toThrow("The JSON value could not be converted to IssueActivity.");
  });

  test("a short integer token still reads, -0 as 0", () => {
    const read = JsonSerializer.deserializeText(
      activityJson('"occurredAtTicks":-0,"durationTicks":42'),
      ctx.issueActivity,
    )!;

    expect(read.occurredAtTicks).toBe(0n);
    expect(read.durationTicks).toBe(42n);
  });
});
