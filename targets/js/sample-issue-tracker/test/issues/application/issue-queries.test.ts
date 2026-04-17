import { describe, expect, test } from "bun:test";
import {
  openIssues,
  statusCounts,
  issuesForAssignee,
  readyForReview,
} from "#/issues/application/issue-queries";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { UserId } from "#/shared-kernel/user-id";
import { makeIssue } from "../../helpers";

describe("IssueQueries", () => {
  test("openIssues filters out closed issues", () => {
    const issues = [
      makeIssue({ title: "Open A", priority: IssuePriority.High }),
      makeIssue({ title: "Open B", priority: IssuePriority.Medium }),
      makeIssue({ title: "Closed", priority: IssuePriority.Urgent }),
    ];
    issues[2]!.status = IssueStatus.Done;

    const result = openIssues(issues);
    expect(result.length).toBe(2);
    expect(result.find(i => i.title === "Closed")).toBeUndefined();
  });

  test("openIssues orderByDescending then thenBy is applied", () => {
    const issues = [
      makeIssue({ title: "B", priority: IssuePriority.Low }),
      makeIssue({ title: "A", priority: IssuePriority.Urgent }),
      makeIssue({ title: "C", priority: IssuePriority.Urgent }),
    ];
    const result = openIssues(issues);
    expect(result.length).toBe(3);
    expect(result[0]?.priority).toBe(IssuePriority.Urgent);
    expect(result[1]?.priority).toBe(IssuePriority.Urgent);
    expect(result[0]?.title).toBe("A");
    expect(result[1]?.title).toBe("C");
    expect(result[2]?.priority).toBe(IssuePriority.Low);
  });

  test("statusCounts groups issues by status", () => {
    const issues = [
      makeIssue({ status: IssueStatus.Backlog }),
      makeIssue({ status: IssueStatus.Backlog }),
      makeIssue({ status: IssueStatus.Ready }),
    ];
    const counts = statusCounts(issues);
    expect(counts.get(IssueStatus.Backlog)).toBe(2);
    expect(counts.get(IssueStatus.Ready)).toBe(1);
  });

  test("issuesForAssignee filters by assignee", () => {
    const alice = UserId.create("alice");
    const bob = UserId.create("bob");
    const issues = [
      makeIssue({ assigneeId: alice }),
      makeIssue({ assigneeId: bob }),
      makeIssue({ assigneeId: alice }),
    ];
    expect(issuesForAssignee(issues, alice).length).toBe(2);
    expect(issuesForAssignee(issues, bob).length).toBe(1);
  });

  test("readyForReview returns high/urgent in-progress or in-review", () => {
    const issues = [
      makeIssue({ status: IssueStatus.InProgress, priority: IssuePriority.High }),
      makeIssue({ status: IssueStatus.InReview, priority: IssuePriority.Urgent }),
      makeIssue({ status: IssueStatus.InProgress, priority: IssuePriority.Low }),
      makeIssue({ status: IssueStatus.Backlog, priority: IssuePriority.Urgent }),
    ];
    const result = readyForReview(issues, 10);
    expect(result.length).toBe(2);
  });

  test("readyForReview respects limit", () => {
    const issues = [
      makeIssue({ status: IssueStatus.InProgress, priority: IssuePriority.High }),
      makeIssue({ status: IssueStatus.InProgress, priority: IssuePriority.High }),
      makeIssue({ status: IssueStatus.InProgress, priority: IssuePriority.High }),
    ];
    expect(readyForReview(issues, 2).length).toBe(2);
  });
});
