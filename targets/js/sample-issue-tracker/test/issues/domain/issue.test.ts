import { describe, expect, test } from "bun:test";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { UserId } from "#/shared-kernel/user-id";
import { makeIssue } from "../../helpers";

describe("Issue", () => {
  test("starts in Backlog status", () => {
    const issue = makeIssue();
    expect(issue.status).toBe(IssueStatus.Backlog);
  });

  test("isClosed is true for Done/Cancelled", () => {
    const issue = makeIssue();
    expect(issue.isClosed).toBe(false);
    issue.status = IssueStatus.Done;
    expect(issue.isClosed).toBe(true);
    issue.status = IssueStatus.Cancelled;
    expect(issue.isClosed).toBe(true);
  });

  test("rename() changes title", () => {
    const issue = makeIssue();
    issue.rename("New title");
    expect(issue.title).toBe("New title");
  });

  test("changePriority() updates priority", () => {
    const issue = makeIssue();
    issue.changePriority(IssuePriority.Urgent);
    expect(issue.priority).toBe(IssuePriority.Urgent);
  });

  test("assignTo() / unassign() work correctly", () => {
    const issue = makeIssue();
    expect(issue.assigneeId).toBeNull();
    issue.assignTo(UserId.create("alice"));
    expect(issue.assigneeId).toBe("alice" as any);
    issue.unassign();
    expect(issue.assigneeId).toBeNull();
  });

  test("addComment with author + message", () => {
    const issue = makeIssue();
    issue.addComment(UserId.create("bob"), "Looks good");
    expect(issue.commentCount).toBe(1);
    expect(issue.comments[0]?.message).toBe("Looks good");
  });

  test("transitionTo() valid transition", () => {
    const issue = makeIssue();
    issue.transitionTo(IssueStatus.Ready, UserId.create("alice"));
    expect(issue.status).toBe(IssueStatus.Ready);
    expect(issue.commentCount).toBe(1);
  });

  test("transitionTo() invalid transition throws", () => {
    const issue = makeIssue();
    expect(() => {
      issue.transitionTo(IssueStatus.Done, UserId.create("alice"));
    }).toThrow();
  });

  test("planForSprint() / removeFromSprint()", () => {
    const issue = makeIssue();
    issue.planForSprint("SPRINT-1");
    expect(issue.sprintKey).toBe("SPRINT-1");
    issue.removeFromSprint();
    expect(issue.sprintKey).toBeNull();
  });
});
