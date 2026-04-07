import { describe, expect, test } from "bun:test";
import { IssueWorkflow } from "#/issues/domain/issue-workflow";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { makeIssue } from "../../helpers";

describe("IssueWorkflow", () => {
  test("getAllowedTransitions from Backlog", () => {
    const result = IssueWorkflow.getAllowedTransitions(IssueStatus.Backlog);
    expect(result).toContain(IssueStatus.Ready);
    expect(result).toContain(IssueStatus.Cancelled);
  });

  test("getAllowedTransitions from Ready", () => {
    const result = IssueWorkflow.getAllowedTransitions(IssueStatus.Ready);
    expect(result).toContain(IssueStatus.InProgress);
    expect(result).toContain(IssueStatus.Cancelled);
  });

  test("getAllowedTransitions from Done is empty", () => {
    expect(IssueWorkflow.getAllowedTransitions(IssueStatus.Done)).toEqual([]);
  });

  test("canTransition validates allowed transitions", () => {
    expect(IssueWorkflow.canTransition(IssueStatus.Backlog, IssueStatus.Ready)).toBe(true);
    expect(IssueWorkflow.canTransition(IssueStatus.Backlog, IssueStatus.Done)).toBe(false);
    expect(IssueWorkflow.canTransition(IssueStatus.Ready, IssueStatus.InProgress)).toBe(true);
    expect(IssueWorkflow.canTransition(IssueStatus.InProgress, IssueStatus.InReview)).toBe(true);
  });

  test("describeLane returns correct lane for status", () => {
    const issue = makeIssue({ status: IssueStatus.Backlog });
    expect(IssueWorkflow.describeLane(issue)).toBe("triage");

    issue.status = IssueStatus.InProgress;
    expect(IssueWorkflow.describeLane(issue)).toBe("building");

    issue.status = IssueStatus.Done;
    expect(IssueWorkflow.describeLane(issue)).toBe("done");
  });

  test("describeLane: ready + urgent → expedite", () => {
    const issue = makeIssue({ status: IssueStatus.Ready, priority: IssuePriority.Urgent });
    expect(IssueWorkflow.describeLane(issue)).toBe("expedite");
  });
});
