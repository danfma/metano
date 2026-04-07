import { describe, expect, test } from "bun:test";
import { IssueId } from "#/issues/domain/issue-id";

describe("IssueId (InlineWrapper)", () => {
  test("new_ generates unique values", () => {
    const a = IssueId.new_();
    const b = IssueId.new_();
    expect(a).not.toBe(b);
  });

  test("create wraps a string", () => {
    const id = IssueId.create("test-123");
    expect(id).toBe("test-123" as any);
  });
});
