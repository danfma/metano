/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
export const IssueStatus = {
  Backlog: "backlog",
  Ready: "ready",
  InProgress: "in-progress",
  InReview: "in-review",
  Done: "done",
  Cancelled: "cancelled",
} as const;

export type IssueStatus = (typeof IssueStatus)[keyof typeof IssueStatus];
