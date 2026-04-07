import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
import { Comment } from "#/issues/domain/comment";
import { UserId } from "#/shared-kernel/user-id";

describe("Comment", () => {
  test("system() factory creates a system comment", () => {
    const c = Comment.system("Status changed", Temporal.Now.zonedDateTimeISO());
    expect(c.isSystem).toBe(true);
  });

  test("equals() compares structurally", () => {
    const ts = Temporal.Now.zonedDateTimeISO();
    const a = new Comment(UserId.create("alice"), "hi", ts);
    const b = new Comment(UserId.create("alice"), "hi", ts);
    expect(a.equals(b)).toBe(true);
  });
});
