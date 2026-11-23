/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { EnumType } from "metano-runtime";

export const IssueActivityKind = {
  Created: "created",
  Assigned: "assigned",
  StatusChanged: "status-changed",
  Commented: "commented",
  Closed: "closed",
} as const;

export type IssueActivityKind = (typeof IssueActivityKind)[keyof typeof IssueActivityKind];

export const IssueActivityKind$runtimeType: EnumType<IssueActivityKind> = {
  kind: "stringEnum",
  name: "IssueActivityKind",
  fullName: "SampleIssueTracker.Issues.Domain.IssueActivityKind",
  namespace: "SampleIssueTracker.Issues.Domain",
  values: ["created", "assigned", "status-changed", "commented", "closed"],
  names: ["Created", "Assigned", "StatusChanged", "Commented", "Closed"],
  isFlags: false,
  default: () => "created",
  test: (v: unknown): v is IssueActivityKind =>
    v === "created" ||
    v === "assigned" ||
    v === "status-changed" ||
    v === "commented" ||
    v === "closed",
};
