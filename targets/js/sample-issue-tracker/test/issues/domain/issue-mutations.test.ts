import { describe, expect, test } from "bun:test";
import { Issue } from "#/issues/domain/issue";
import { IssueId } from "#/issues/domain/issue-id";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueType } from "#/issues/domain/issue-type";
import { UserId } from "#/shared-kernel/user-id";
import { makeIssue } from "../../helpers";

// Covers invariants the existing issue.test.ts doesn't pin:
//   * UpdatedAt advances on every mutation
//   * CreatedAt + Id + Type stay constant across mutations
//   * Multiple comments accumulate in insertion order
//   * transitionTo records a system comment containing the prev/next
//     status names so downstream audit readers can parse the trail

describe("Issue mutations", () => {
  test("CreatedAt and UpdatedAt start within one second of each other", () => {
    const issue = makeIssue();
    // Each field initializes via its own `Temporal.Now.zonedDateTimeISO()`
    // call, so they can legitimately straddle a wall-clock second
    // boundary. Assert the absolute delta stays within a second instead
    // of pinning equal whole seconds (which would flake once per second
    // in CI).
    const createdAtNs = issue.createdAt.epochNanoseconds;
    const updatedAtNs = issue.updatedAt.epochNanoseconds;
    const deltaNs =
      createdAtNs >= updatedAtNs
        ? createdAtNs - updatedAtNs
        : updatedAtNs - createdAtNs;
    expect(deltaNs <= 1_000_000_000n).toBe(true);
  });

  test("rename does not decrease UpdatedAt", () => {
    // Weaker than "advances" — `Temporal.Now` resolution can coincide
    // for two synchronous calls, so a strict `>` would flake. What we
    // actually guarantee (and want to pin): `touch` never rewinds the
    // timestamp.
    const issue = makeIssue();
    const before = issue.updatedAt.epochNanoseconds;
    issue.rename("New title");
    expect(issue.updatedAt.epochNanoseconds >= before).toBe(true);
    // Plus the rename itself took effect.
    expect(issue.title).toBe("New title");
  });

  test("CreatedAt never changes after mutations", () => {
    const issue = makeIssue();
    const createdAt = issue.createdAt.epochNanoseconds;
    issue.rename("first");
    issue.describe("second");
    issue.changePriority(IssuePriority.High);
    issue.assignTo(UserId.create("alice"));
    expect(issue.createdAt.epochNanoseconds).toBe(createdAt);
  });

  test("Id is read-only across mutations", () => {
    const issue = makeIssue();
    const id = issue.id;
    issue.rename("x");
    issue.transitionTo(IssueStatus.Ready, UserId.create("alice"));
    expect(issue.id).toBe(id);
  });

  test("Type is read-only across mutations", () => {
    const id = IssueId.new_();
    const issue = new Issue(
      id,
      "Title",
      "Description",
      IssueType.Bug,
      IssuePriority.Medium,
    );
    const type = issue.type;
    issue.rename("x");
    issue.changePriority(IssuePriority.High);
    expect(issue.type).toBe(type);
  });

  test("addComment accumulates in insertion order", () => {
    const issue = makeIssue();
    issue.addComment(UserId.create("alice"), "first");
    issue.addComment(UserId.create("bob"), "second");
    issue.addComment(UserId.create("alice"), "third");
    expect(issue.commentCount).toBe(3);
    expect(issue.comments[0]?.message).toBe("first");
    expect(issue.comments[1]?.message).toBe("second");
    expect(issue.comments[2]?.message).toBe("third");
  });

  test("transitionTo records a system comment describing the change", () => {
    const issue = makeIssue();
    issue.transitionTo(IssueStatus.Ready, UserId.create("alice"));
    const note = issue.comments[0]?.message ?? "";
    // C# enum values render through string interpolation via their
    // ToString(); the transpiled StringEnum emits lowercase literals
    // (`"backlog"`, `"ready"`) because IssueStatus has `[Name("...")]`
    // overrides that lower-case each member. Assert against the
    // emitted literals, not the C# identifier case.
    expect(note).toContain(IssueStatus.Backlog);
    expect(note).toContain(IssueStatus.Ready);
    expect(note).toContain("alice");
  });

  test("lane tracks status + priority combinations", () => {
    const issue = makeIssue({
      status: IssueStatus.Ready,
      priority: IssuePriority.Urgent,
    });
    expect(issue.lane).toBe("expedite");

    issue.status = IssueStatus.Ready;
    issue.changePriority(IssuePriority.Medium);
    expect(issue.lane).toBe("ready");

    issue.status = IssueStatus.InProgress;
    expect(issue.lane).toBe("building");
  });
});
