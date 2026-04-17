import { Enumerable } from "metano-runtime";
import type { Issue } from "./issue";
import { IssuePriority } from "./issue-priority";
import { IssueStatus } from "./issue-status";

export class IssueWorkflow {
  constructor() { }

  static getAllowedTransitions(currentStatus: IssueStatus): IssueStatus[] {
    return currentStatus === IssueStatus.Backlog ? [IssueStatus.Ready, IssueStatus.Cancelled] : currentStatus === IssueStatus.Ready ? [IssueStatus.InProgress, IssueStatus.Cancelled] : currentStatus === IssueStatus.InProgress ? [IssueStatus.InReview, IssueStatus.Backlog, IssueStatus.Cancelled] : currentStatus === IssueStatus.InReview ? [IssueStatus.Done, IssueStatus.InProgress, IssueStatus.Cancelled] : [];
  }

  static canTransition(currentStatus: IssueStatus, nextStatus: IssueStatus): boolean {
    return Enumerable.from(IssueWorkflow.getAllowedTransitions(currentStatus)).contains(nextStatus);
  }

  static describeLane(issue: Issue): string {
    return issue.status === IssueStatus.Backlog ? "triage" : issue.status === IssueStatus.Ready && issue.priority === IssuePriority.Urgent ? "expedite" : issue.status === IssueStatus.Ready ? "ready" : issue.status === IssueStatus.InProgress ? "building" : issue.status === IssueStatus.InReview ? "review" : issue.status === IssueStatus.Done ? "done" : "cancelled";
  }
}
