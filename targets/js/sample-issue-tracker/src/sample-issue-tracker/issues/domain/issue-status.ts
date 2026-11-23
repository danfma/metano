/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { EnumType } from "metano-runtime";

export const IssueStatus = {
  Backlog: "backlog",
  Ready: "ready",
  InProgress: "in-progress",
  InReview: "in-review",
  Done: "done",
  Cancelled: "cancelled",
} as const;

export type IssueStatus = (typeof IssueStatus)[keyof typeof IssueStatus];

export const IssueStatus$runtimeType: EnumType<IssueStatus> = {
  kind: "stringEnum",
  name: "IssueStatus",
  fullName: "SampleIssueTracker.Issues.Domain.IssueStatus",
  namespace: "SampleIssueTracker.Issues.Domain",
  values: ["backlog", "ready", "in-progress", "in-review", "done", "cancelled"],
  names: ["Backlog", "Ready", "InProgress", "InReview", "Done", "Cancelled"],
  isFlags: false,
  default: () => "backlog",
  test: (v: unknown): v is IssueStatus =>
    v === "backlog" ||
    v === "ready" ||
    v === "in-progress" ||
    v === "in-review" ||
    v === "done" ||
    v === "cancelled",
};
