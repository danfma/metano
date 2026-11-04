import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
import { Decimal } from "decimal.js";
import { type JsonConverter, JsonSerializer } from "metano-runtime/system/json";
import {
  Comment,
  IssueId,
  IssuePriority,
  IssueSnapshot,
  IssueStatus,
  IssueType,
} from "#/sample-issue-tracker/issues/domain";
import { JsonContext } from "#/sample-issue-tracker/json-context";
import { UserId } from "#/sample-issue-tracker/shared-kernel";

// ─── Fixtures ────────────────────────────────────────────────────────────────

// `??` collapses null into the default, which is exactly wrong for the
// nullable fields in these fixtures (we need to distinguish "caller omitted
// assigneeId → default to alice" from "caller explicitly passed null"). Use
// `undefined` as the only sentinel for "not provided" and check the key's
// presence via `in`.
function makeSnapshot(
  overrides: {
    id?: IssueId;
    assigneeId?: UserId | null;
    sprintKey?: string | null;
    estimatedHours?: Decimal;
    comments?: Comment[];
  } = {},
): IssueSnapshot {
  return new IssueSnapshot(
    overrides.id ?? IssueId.create("issue-001"),
    "Ship the serializer end-to-end",
    "Finish Phase 5 of the JSON serializer plan",
    IssueType.Chore,
    IssuePriority.High,
    IssueStatus.InProgress,
    "assigneeId" in overrides ? (overrides.assigneeId ?? null) : UserId.create("alice"),
    "sprintKey" in overrides ? (overrides.sprintKey ?? null) : "sprint-42",
    Temporal.ZonedDateTime.from("2026-04-11T10:30:00+00:00[UTC]"),
    Temporal.ZonedDateTime.from("2026-04-11T11:00:00+00:00[UTC]"),
    overrides.estimatedHours ?? new Decimal("1.5"),
    overrides.comments ?? [
      new Comment(
        UserId.create("bob"),
        "First comment",
        Temporal.ZonedDateTime.from("2026-04-11T10:45:00+00:00[UTC]"),
      ),
    ],
  );
}

// ─── Default context ─────────────────────────────────────────────────────────

describe("JsonContext — default serialization", () => {
  const ctx = JsonContext.default;

  test("default getter returns a singleton", () => {
    expect(JsonContext.default).toBe(ctx);
  });

  test("serializes primitives, enums, and branded IDs to JSON-safe values", () => {
    const snapshot = makeSnapshot();
    const json = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);

    expect(json.id).toBe("issue-001");
    expect(json.title).toBe("Ship the serializer end-to-end");
    expect(json.description).toBe("Finish Phase 5 of the JSON serializer plan");
    expect(json.type).toBe(IssueType.Chore);
    expect(json.priority).toBe(IssuePriority.High);
    expect(json.status).toBe(IssueStatus.InProgress);
    expect(json.assigneeId).toBe("alice");
    expect(json.sprintKey).toBe("sprint-42");
  });

  test("serializes Temporal fields as ISO strings that round-trip", () => {
    const snapshot = makeSnapshot();
    const json = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);

    expect(typeof json.createdAt).toBe("string");
    expect(typeof json.updatedAt).toBe("string");
    const reparsed = Temporal.ZonedDateTime.from(json.createdAt as string);
    expect(reparsed.epochMilliseconds).toBe(snapshot.createdAt.epochMilliseconds);
  });

  test("serializes decimal as a JSON number by default", () => {
    const snapshot = makeSnapshot();
    const json = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);

    expect(JSON.stringify(json.estimatedHours)).toBe("1.5");
  });

  // System.Text.Json writes 12345678901234567890.123456789m in full; a JS
  // number would print 12345678901234567000.
  test("writes a decimal with its exact digits, as System.Text.Json does", () => {
    const snapshot = makeSnapshot({
      estimatedHours: new Decimal("12345678901234567890.123456789"),
    });

    const json = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);

    expect(JSON.stringify(json.estimatedHours)).toBe("12345678901234567890.123456789");
  });

  test("emits null for nullable fields when absent", () => {
    const snapshot = makeSnapshot({ assigneeId: null, sprintKey: null });
    const json = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);

    expect(json.assigneeId).toBeNull();
    expect(json.sprintKey).toBeNull();
  });

  test("serializes nested Comment array via the ref spec getter", () => {
    const snapshot = makeSnapshot();
    const json = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);

    const comments = json.comments as Array<Record<string, unknown>>;
    expect(comments).toHaveLength(1);
    expect(comments[0]?.authorId).toBe("bob");
    expect(comments[0]?.message).toBe("First comment");
    expect(comments[0]?.isSystem).toBe(false);
    expect(typeof comments[0]?.createdAt).toBe("string");
  });

  test("serializes an empty comments array without nesting", () => {
    const snapshot = makeSnapshot({ comments: [] });
    const json = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);

    expect(json.comments).toEqual([]);
  });

  test("round-trips through JSON.stringify + JSON.parse", () => {
    const snapshot = makeSnapshot();

    const serialized = JsonSerializer.serialize(snapshot, ctx.issueSnapshot);
    const wire = JSON.stringify(serialized);
    const parsed = JSON.parse(wire);
    const deserialized = JsonSerializer.deserialize(parsed, ctx.issueSnapshot);

    // Branded-type fields compare via their primitive string content, so casting
    // to `unknown` sidesteps TS's nominal-equality complaint while still testing
    // the runtime equality that matters.
    expect(deserialized.id as unknown).toBe(snapshot.id as unknown);
    expect(deserialized.title).toBe(snapshot.title);
    expect(deserialized.description).toBe(snapshot.description);
    expect(deserialized.type).toBe(snapshot.type);
    expect(deserialized.priority).toBe(snapshot.priority);
    expect(deserialized.status).toBe(snapshot.status);
    expect(deserialized.assigneeId as unknown).toBe(snapshot.assigneeId as unknown);
    expect(deserialized.sprintKey).toBe(snapshot.sprintKey);
    expect(deserialized.createdAt.epochMilliseconds).toBe(snapshot.createdAt.epochMilliseconds);
    expect(deserialized.updatedAt.epochMilliseconds).toBe(snapshot.updatedAt.epochMilliseconds);
    expect(deserialized.comments).toHaveLength(1);
    expect(deserialized.comments[0]?.authorId as unknown).toBe(
      snapshot.comments[0]?.authorId as unknown,
    );
    expect(deserialized.comments[0]?.message).toBe(snapshot.comments[0]?.message);
    expect(deserialized.estimatedHours).toBeInstanceOf(Decimal);
    expect(deserialized.estimatedHours.equals(snapshot.estimatedHours)).toBe(true);
  });

  test("deserializes a raw JSON payload (as if it came from HTTP)", () => {
    const payload = {
      id: "from-wire",
      title: "From the wire",
      description: "A server pushed this object",
      type: IssueType.Bug,
      priority: IssuePriority.Urgent,
      status: IssueStatus.InReview,
      assigneeId: "charlie",
      sprintKey: null,
      createdAt: "2026-04-11T09:00:00+00:00[UTC]",
      updatedAt: "2026-04-11T09:15:00+00:00[UTC]",
      estimatedHours: 2,
      comments: [],
    };

    const issue = JsonSerializer.deserialize(payload, ctx.issueSnapshot);

    expect(issue.id).toBe(IssueId.create("from-wire"));
    expect(issue.type).toBe(IssueType.Bug);
    expect(issue.priority).toBe(IssuePriority.Urgent);
    expect(issue.status).toBe(IssueStatus.InReview);
    expect(issue.assigneeId).toBe(UserId.create("charlie"));
    expect(issue.sprintKey).toBeNull();
    expect(issue.createdAt.epochMilliseconds).toBe(
      Temporal.ZonedDateTime.from(payload.createdAt).epochMilliseconds,
    );
    expect(issue.estimatedHours).toBeInstanceOf(Decimal);
    expect(issue.estimatedHours.toString()).toBe("2");

    const written = JsonSerializer.serialize(issue, ctx.issueSnapshot);

    expect(JSON.stringify(written.estimatedHours)).toBe("2");
  });

  // The context keeps System.Text.Json's default NumberHandling (Strict).
  test("rejects a decimal sent as a JSON string, as System.Text.Json does", () => {
    const payload = {
      ...JSON.parse(JSON.stringify(JsonSerializer.serialize(makeSnapshot(), ctx.issueSnapshot))),
      estimatedHours: "2.5",
    };

    expect(() => JsonSerializer.deserialize(payload, ctx.issueSnapshot)).toThrow(
      "The JSON value could not be converted to System.Decimal.",
    );
  });
});

// ─── Custom converter: decimal → string for financial precision ──────────────

describe("JsonContext — decimal-as-string custom converter", () => {
  // Canonical custom-converter scenario. By default the runtime emits
  // Decimals as JSON numbers, which silently loses IEEE-754 precision for
  // financial values. A context with this converter emits (and accepts)
  // decimals as JSON strings instead, preserving the full precision across
  // the wire.
  const decimalAsString: JsonConverter = {
    kind: "decimal",
    serialize: (value) => (value as Decimal).toString(),
    deserialize: (value) => new Decimal(value as string),
  };

  const preciseCtx = new JsonContext({ converters: [decimalAsString] });
  // The BoundSerializer pattern is how context-bound converters flow through
  // serialize/deserialize. Static JsonSerializer.serialize without the third
  // arg uses NO converters — it's the "direct-spec, no context" entry point.
  const precise = JsonSerializer.withContext(preciseCtx);

  // 0.1 + 0.2 is exactly 0.3 as Decimal, but 0.30000000000000004 as a JS
  // number. Choosing this value on purpose so the test fails loudly if the
  // converter ever regresses and the wire goes back to number.
  const preciseValue = new Decimal("0.1").plus(new Decimal("0.2"));

  test("serializes decimal as a JSON string", () => {
    const snapshot = makeSnapshot({ estimatedHours: preciseValue });
    const json = precise.serialize(snapshot, preciseCtx.issueSnapshot);

    expect(typeof json.estimatedHours).toBe("string");
    expect(json.estimatedHours).toBe("0.3");
  });

  test("deserializes a JSON string back into a Decimal instance", () => {
    const payload = {
      id: "financial-001",
      title: "Precise hours",
      description: "String-encoded decimal",
      type: IssueType.Chore,
      priority: IssuePriority.Medium,
      status: IssueStatus.Backlog,
      assigneeId: null,
      sprintKey: null,
      createdAt: "2026-04-11T10:00:00+00:00[UTC]",
      updatedAt: "2026-04-11T10:00:00+00:00[UTC]",
      estimatedHours: "0.1",
      comments: [],
    };

    const issue = precise.deserialize(payload, preciseCtx.issueSnapshot);

    expect(issue.estimatedHours).toBeInstanceOf(Decimal);
    expect((issue.estimatedHours as Decimal).toString()).toBe("0.1");
  });

  test("round-trip preserves full Decimal precision (no IEEE-754 loss)", () => {
    const snapshot = makeSnapshot({ estimatedHours: preciseValue });

    const serialized = precise.serialize(snapshot, preciseCtx.issueSnapshot);
    const wire = JSON.stringify(serialized);
    const parsed = JSON.parse(wire);
    const deserialized = precise.deserialize(parsed, preciseCtx.issueSnapshot);

    // If the wire had gone through a JS number this would fail — the lossy
    // 0.30000000000000004 doesn't equal 0.3 as a Decimal.
    const actual = deserialized.estimatedHours as Decimal;
    expect(actual.toString()).toBe("0.3");
    expect(actual.equals(new Decimal("0.3"))).toBe(true);
  });

  test("default context is unaffected — still emits decimal as a number", () => {
    const snapshot = makeSnapshot({ estimatedHours: preciseValue });
    const json = JsonSerializer.serialize(snapshot, JsonContext.default.issueSnapshot);

    expect(JSON.stringify(json.estimatedHours)).toBe("0.3");
  });

  test("custom context does not leak the converter into another context", () => {
    // Two contexts side by side — one with the converter, one without.
    // Their behavior must stay independent, even when they share the same
    // JsonContext class.
    const snapshot = makeSnapshot({ estimatedHours: preciseValue });

    const preciseJson = precise.serialize(snapshot, preciseCtx.issueSnapshot);
    const defaultJson = JsonSerializer.withContext(JsonContext.default).serialize(
      snapshot,
      JsonContext.default.issueSnapshot,
    );

    expect(typeof preciseJson.estimatedHours).toBe("string");
    expect(JSON.stringify(defaultJson.estimatedHours)).toBe("0.3");
  });
});

// ─── Value roots ─────────────────────────────────────────────────────────────

describe("JsonContext — IReadOnlyList<Comment> value root", () => {
  const ctx = JsonContext.default;

  const comments = [
    new Comment(
      UserId.create("bob"),
      "First comment",
      Temporal.ZonedDateTime.from("2026-04-11T10:45:00+00:00[UTC]"),
    ),
    new Comment(
      UserId.create("carol"),
      "Second comment",
      Temporal.ZonedDateTime.from("2026-04-11T11:15:00+00:00[UTC]"),
    ),
  ];

  test("serializes the list element by element through the comment spec", () => {
    const json = JsonSerializer.serialize(comments, ctx.iReadOnlyListComment);

    expect(json).toEqual([
      {
        authorId: "bob",
        message: "First comment",
        createdAt: String(comments[0]?.createdAt),
        isSystem: false,
      },
      {
        authorId: "carol",
        message: "Second comment",
        createdAt: String(comments[1]?.createdAt),
        isSystem: false,
      },
    ]);
  });

  test("round-trips through JSON text into Comment instances", () => {
    const text = JSON.stringify(JsonSerializer.serialize(comments, ctx.iReadOnlyListComment));
    const restored = JsonSerializer.deserialize(JSON.parse(text), ctx.iReadOnlyListComment);

    expect(restored).toHaveLength(2);
    expect(restored?.[0]).toBeInstanceOf(Comment);
    expect(restored?.[1]?.message).toBe("Second comment");
    expect(restored?.[1]?.createdAt.epochMilliseconds).toBe(
      comments[1]?.createdAt.epochMilliseconds,
    );
  });

  test("reads JSON null as null", () => {
    expect(JsonSerializer.deserialize(null, ctx.iReadOnlyListComment)).toBeNull();
  });
});

describe("JsonContext — UserId deserializes through its [JsonFactory]", () => {
  const ctx = JsonContext.default;

  const commentJson = (authorId: string) => ({
    authorId,
    message: "Hello",
    createdAt: "2026-04-11T10:45:00+00:00[UTC]",
    isSystem: false,
  });

  test("a valid id is normalized by UserId.from", () => {
    const comment = JsonSerializer.deserialize(commentJson("  dave "), ctx.comment);

    expect(comment.authorId).toBe(UserId.create("dave"));
  });

  test("a blank id throws the validation error instead of being accepted", () => {
    expect(() => JsonSerializer.deserialize(commentJson("   "), ctx.comment)).toThrow(
      "A user id cannot be blank.",
    );
  });

  test("a blank id inside a list value root throws as well", () => {
    expect(() =>
      JsonSerializer.deserialize([commentJson("erin"), commentJson("")], ctx.iReadOnlyListComment),
    ).toThrow("A user id cannot be blank.");
  });
});
