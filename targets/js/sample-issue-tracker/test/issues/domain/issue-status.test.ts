import { describe, expect, test } from "bun:test";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueType } from "#/issues/domain/issue-type";

describe("StringEnums", () => {
  test("IssueStatus values are accessible as object members", () => {
    expect(IssueStatus.Backlog).toBe("backlog");
    expect(IssueStatus.Ready).toBe("ready");
    expect(IssueStatus.InProgress).toBe("in-progress");
    expect(IssueStatus.Done).toBe("done");
  });

  test("IssuePriority values are accessible", () => {
    expect(IssuePriority.Low).toBe("low");
    expect(IssuePriority.Urgent).toBe("urgent");
  });

  test("IssueType values are accessible", () => {
    expect(IssueType.Story).toBe("story");
    expect(IssueType.Bug).toBe("bug");
  });
});
