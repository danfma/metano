import { describe, expect, test } from "bun:test";
import { UserId } from "#/shared-kernel/user-id";

describe("UserId (InlineWrapper)", () => {
  test("create produces a branded string", () => {
    const id = UserId.create("alice");
    expect(typeof id).toBe("string");
    expect(id).toBe("alice" as any);
  });

  test("system returns 'system'", () => {
    expect(UserId.system()).toBe("system" as any);
  });

  test("new_ generates a uuid-like string", () => {
    const id = UserId.new_();
    expect(typeof id).toBe("string");
    expect((id as string).length).toBeGreaterThan(0);
    expect((id as string)).not.toContain("-");
  });

  test("UserId is comparable with ===", () => {
    const a = UserId.create("alice");
    const b = UserId.create("alice");
    expect(a === b).toBe(true);
  });
});
