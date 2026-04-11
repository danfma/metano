import { describe, expect, test } from "bun:test";
import { UUID } from "#/system/uuid.ts";

describe("UUID.create", () => {
  test("wraps a string as UUID without validation", () => {
    const id = UUID.create("abc-123");
    expect(id).toBe("abc-123" as UUID);
  });
});

describe("UUID.newUuid", () => {
  test("returns a canonical 8-4-4-4-12 form", () => {
    const id = UUID.newUuid();
    expect(id).toMatch(
      /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/,
    );
  });

  test("produces distinct values on successive calls", () => {
    const a = UUID.newUuid();
    const b = UUID.newUuid();
    expect(a).not.toBe(b);
  });
});

describe("UUID.newCompact", () => {
  test("returns a 32-char hex string with no hyphens", () => {
    const id = UUID.newCompact();
    expect(id).toMatch(/^[0-9a-fA-F]{32}$/);
    expect(id).not.toContain("-");
  });

  test("produces distinct values on successive calls", () => {
    const a = UUID.newCompact();
    const b = UUID.newCompact();
    expect(a).not.toBe(b);
  });
});

describe("UUID.empty", () => {
  test("is the canonical all-zero UUID", () => {
    expect(UUID.empty).toBe("00000000-0000-0000-0000-000000000000" as UUID);
  });
});

describe("UUID.isUuid", () => {
  test("accepts canonical form", () => {
    expect(UUID.isUuid("550e8400-e29b-41d4-a716-446655440000")).toBe(true);
  });

  test("accepts compact form (no hyphens)", () => {
    expect(UUID.isUuid("550e8400e29b41d4a716446655440000")).toBe(true);
  });

  test("accepts Empty UUID", () => {
    expect(UUID.isUuid("00000000-0000-0000-0000-000000000000")).toBe(true);
  });

  test("accepts uppercase hex", () => {
    expect(UUID.isUuid("550E8400-E29B-41D4-A716-446655440000")).toBe(true);
  });

  test("rejects strings with invalid length", () => {
    expect(UUID.isUuid("550e8400-e29b-41d4-a716-44665544")).toBe(false);
    expect(UUID.isUuid("too-short")).toBe(false);
  });

  test("rejects strings with non-hex characters", () => {
    expect(UUID.isUuid("gggggggg-gggg-gggg-gggg-gggggggggggg")).toBe(false);
  });

  test("rejects non-strings", () => {
    expect(UUID.isUuid(42)).toBe(false);
    expect(UUID.isUuid(null)).toBe(false);
    expect(UUID.isUuid(undefined)).toBe(false);
    expect(UUID.isUuid({})).toBe(false);
    expect(UUID.isUuid([])).toBe(false);
  });

  test("narrows the type on true branch", () => {
    const value: unknown = "550e8400-e29b-41d4-a716-446655440000";
    if (UUID.isUuid(value)) {
      // Type should be UUID here — assignable to string
      const s: string = value;
      expect(s).toBe("550e8400-e29b-41d4-a716-446655440000");
    }
  });
});

describe("UUID — brand semantics", () => {
  test("brand erases at runtime", () => {
    const id = UUID.create("hello");
    expect(typeof id).toBe("string");
    expect(id.length).toBe(5);
  });

  test("can be used with standard string methods", () => {
    const id = UUID.newUuid();
    expect(id.startsWith(id[0]!)).toBe(true);
    expect(id.toUpperCase()).toBe(id.toUpperCase());
  });
});
