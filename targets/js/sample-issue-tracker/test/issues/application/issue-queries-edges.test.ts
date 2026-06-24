import { describe, expect, test } from "bun:test";
import {
  openIssues,
  statusCounts,
  issuesForAssignee,
  readyForReview,
} from "#/sample-issue-tracker/issues/application/issue-queries";
import { IssueStatus } from "#/sample-issue-tracker/issues/domain/issue-status";
import { IssuePriority } from "#/sample-issue-tracker/issues/domain/issue-priority";
import { UserId } from "#/sample-issue-tracker/shared-kernel/user-id";
import { makeIssue } from "../../helpers";

// Edges the base issue-queries.test.ts doesn't pin:
//   * empty input returns empty containers
//   * openIssues excludes Done AND Cancelled (both are isClosed = true)
//   * issuesForAssignee applies the C# ordering
//     (status asc, then priority desc)
//   * readyForReview rejects matching-status but wrong-priority
//   * statusCounts leaves unobserved statuses absent from the map

describe("IssueQueries edges", () => {
  test("openIssues on empty list returns empty list", () => {
    expect(openIssues([])).toEqual([]);
  });

  test("openIssues excludes Cancelled as well as Done", () => {
    const issues = [
      makeIssue({ title: "Open", priority: IssuePriority.Medium }),
      makeIssue({ title: "Done", priority: IssuePriority.Medium }),
      makeIssue({ title: "Cancelled", priority: IssuePriority.Medium }),
    ];
    issues[1]!.status = IssueStatus.Done;
    issues[2]!.status = IssueStatus.Cancelled;
    const result = openIssues(issues);
    expect(result.length).toBe(1);
    expect(result[0]?.title).toBe("Open");
  });

  test("issuesForAssignee orders by status ASC then priority DESC", () => {
    const alice = UserId.create("alice");
    const issues = [
      makeIssue({ title: "InReview-Low" }),
      makeIssue({ title: "Backlog-Urgent" }),
      makeIssue({ title: "Backlog-Low" }),
    ];
    issues[0]!.assignTo(alice);
    issues[0]!.status = IssueStatus.InReview;
    issues[0]!.changePriority(IssuePriority.Low);

    issues[1]!.assignTo(alice);
    issues[1]!.status = IssueStatus.Backlog;
    issues[1]!.changePriority(IssuePriority.Urgent);

    issues[2]!.assignTo(alice);
    issues[2]!.status = IssueStatus.Backlog;
    issues[2]!.changePriority(IssuePriority.Low);

    const result = issuesForAssignee(issues, alice);
    // Backlog sorts before InReview (string literal comparison on the
    // generated StringEnum values). Within Backlog, Urgent precedes
    // Low. The final element is the InReview issue.
    expect(result.length).toBe(3);
    expect(result[0]?.title).toBe("Backlog-Urgent");
    expect(result[1]?.title).toBe("Backlog-Low");
    expect(result[2]?.title).toBe("InReview-Low");
  });

  test("readyForReview rejects InProgress with Low priority", () => {
    const issues = [
      makeIssue({ status: IssueStatus.InProgress, priority: IssuePriority.Low }),
      makeIssue({ status: IssueStatus.InProgress, priority: IssuePriority.Medium }),
    ];
    const result = readyForReview(issues, 10);
    expect(result).toEqual([]);
  });

  test("readyForReview rejects InProgress+High but different status", () => {
    const issues = [
      makeIssue({ status: IssueStatus.Backlog, priority: IssuePriority.Urgent }),
      makeIssue({ status: IssueStatus.Done, priority: IssuePriority.Urgent }),
      makeIssue({ status: IssueStatus.Ready, priority: IssuePriority.High }),
    ];
    const result = readyForReview(issues, 10);
    expect(result).toEqual([]);
  });

  test("statusCounts omits unobserved statuses", () => {
    const issues = [
      makeIssue({ status: IssueStatus.Backlog }),
      makeIssue({ status: IssueStatus.Ready }),
    ];
    const counts = statusCounts(issues);
    expect(counts.has(IssueStatus.Backlog)).toBe(true);
    expect(counts.has(IssueStatus.Ready)).toBe(true);
    expect(counts.has(IssueStatus.Done)).toBe(false);
    expect(counts.has(IssueStatus.Cancelled)).toBe(false);
  });
});
