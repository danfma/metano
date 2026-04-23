import { describe, expect, test } from "bun:test";

import { assertTodoCreated, isTodoCreated, isTodoUpdated } from "#/events";

// Demonstrates the [Discriminator("Kind")] narrowing: the generated
// isT short-circuits on `v.kind !== "TypeName"` before walking the
// rest of the shape. A mismatched payload for a different event variant
// exits the guard immediately instead of scanning every field.

describe("TodoCreated discriminator", () => {
  test("isTodoCreated accepts a matching payload", () => {
    const event: unknown = {
      kind: "TodoCreated",
      id: "abc-123",
      title: "Buy milk",
    };
    expect(isTodoCreated(event)).toBe(true);
  });

  test("isTodoCreated rejects a TodoUpdated payload via discriminator", () => {
    // Wire shape for TodoUpdated that would pass the shape check if the
    // discriminator wasn't enforced — it has the same fields plus
    // `completed`. The kind mismatch trips the short-circuit.
    const event: unknown = {
      kind: "TodoUpdated",
      id: "abc-123",
      title: "Buy milk",
      completed: true,
    };
    expect(isTodoCreated(event)).toBe(false);
  });

  test("isTodoCreated rejects unknown discriminator values", () => {
    const event: unknown = {
      kind: "Bogus",
      id: "abc-123",
      title: "Buy milk",
    };
    expect(isTodoCreated(event)).toBe(false);
  });

  test("assertTodoCreated throws when the discriminator mismatches", () => {
    const event: unknown = {
      kind: "TodoUpdated",
      id: "abc-123",
      title: "Buy milk",
      completed: true,
    };
    expect(() => assertTodoCreated(event)).toThrow(TypeError);
    expect(() => assertTodoCreated(event)).toThrow("Value is not a TodoCreated");
  });

  test("isTodoUpdated accepts its own kind", () => {
    // Second variant in the hierarchy uses the same enum but a
    // different expected value — TodoUpdated's guard checks
    // `v.kind !== "TodoUpdated"`.
    const event: unknown = {
      kind: "TodoUpdated",
      id: "abc-123",
      title: null,
      completed: false,
    };
    expect(isTodoUpdated(event)).toBe(true);
  });
});
