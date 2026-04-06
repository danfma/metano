import { Enumerable } from "@meta-sharp/runtime";
import type { Issue } from "./Issue";
import { IssueStatus } from "./IssueStatus";
export class IssueWorkflow {
  constructor() { }

  static getAllowedTransitions(currentStatus: IssueStatus): IssueStatus[] {
    return currentStatus === IssueStatus.Backlog ? Array.of(IssueStatus.Ready, IssueStatus.Cancelled) : currentStatus === IssueStatus.Ready ? Array.of(IssueStatus.InProgress, IssueStatus.Cancelled) : currentStatus === IssueStatus.InProgress ? Array.of(IssueStatus.InReview, IssueStatus.Backlog, IssueStatus.Cancelled) : currentStatus === IssueStatus.InReview ? Array.of(IssueStatus.Done, IssueStatus.InProgress, IssueStatus.Cancelled) : [];
  }

  static canTransition(currentStatus: IssueStatus, nextStatus: IssueStatus): boolean {
    return Enumerable.from(IssueWorkflow.getAllowedTransitions(currentStatus)).contains(nextStatus);
  }

  static describeLane(issue: Issue): string {
    return issue.status === IssueStatus.Backlog ? "triage" : issue.status === IssueStatus.Ready && issue.priority === IssuePriority.Urgent ? "expedite" : issue.status === IssueStatus.Ready ? "ready" : issue.status === IssueStatus.InProgress ? "building" : issue.status === IssueStatus.InReview ? "review" : issue.status === IssueStatus.Done ? "done" : "cancelled";
  }
}
