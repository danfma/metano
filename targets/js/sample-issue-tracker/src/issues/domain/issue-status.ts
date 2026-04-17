export const IssueStatus = {
  Backlog: "backlog",
  Ready: "ready",
  InProgress: "in-progress",
  InReview: "in-review",
  Done: "done",
  Cancelled: "cancelled",
} as const;

export type IssueStatus = typeof IssueStatus[keyof typeof IssueStatus];
