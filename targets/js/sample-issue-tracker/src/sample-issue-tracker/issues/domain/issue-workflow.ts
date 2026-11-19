/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { List, type IReadOnlyList } from "metano-runtime/system/collections";
import { contains, linq } from "metano-runtime/system/linq";
import type { Issue } from "./issue";
import { IssuePriority } from "./issue-priority";
import { IssueStatus } from "./issue-status";

export class IssueWorkflow {
  constructor() {}

  static getAllowedTransitions(currentStatus: IssueStatus): IReadOnlyList<IssueStatus> {
    return currentStatus === IssueStatus.Backlog
      ? List.adopt<IssueStatus>([IssueStatus.Ready, IssueStatus.Cancelled], { fixedSize: true })
      : currentStatus === IssueStatus.Ready
        ? List.adopt<IssueStatus>([IssueStatus.InProgress, IssueStatus.Cancelled], {
            fixedSize: true,
          })
        : currentStatus === IssueStatus.InProgress
          ? List.adopt<IssueStatus>(
              [IssueStatus.InReview, IssueStatus.Backlog, IssueStatus.Cancelled],
              { fixedSize: true },
            )
          : currentStatus === IssueStatus.InReview
            ? List.adopt<IssueStatus>(
                [IssueStatus.Done, IssueStatus.InProgress, IssueStatus.Cancelled],
                { fixedSize: true },
              )
            : List.adopt<IssueStatus>([], { fixedSize: true });
  }

  static canTransition(currentStatus: IssueStatus, nextStatus: IssueStatus): boolean {
    return linq(IssueWorkflow.getAllowedTransitions(currentStatus), contains(nextStatus));
  }

  static describeLane(issue: Issue): string {
    return issue != null && issue.status === IssueStatus.Backlog
      ? "triage"
      : issue != null &&
          issue.status === IssueStatus.Ready &&
          issue.priority === IssuePriority.Urgent
        ? "expedite"
        : issue != null && issue.status === IssueStatus.Ready
          ? "ready"
          : issue != null && issue.status === IssueStatus.InProgress
            ? "building"
            : issue != null && issue.status === IssueStatus.InReview
              ? "review"
              : issue != null && issue.status === IssueStatus.Done
                ? "done"
                : "cancelled";
  }
}
