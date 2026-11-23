/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { EnumType } from "metano-runtime";

export const IssuePriority = {
  Low: "low",
  Medium: "medium",
  High: "high",
  Urgent: "urgent",
} as const;

export type IssuePriority = (typeof IssuePriority)[keyof typeof IssuePriority];

export const IssuePriority$runtimeType: EnumType<IssuePriority> = {
  kind: "stringEnum",
  name: "IssuePriority",
  fullName: "SampleIssueTracker.Issues.Domain.IssuePriority",
  namespace: "SampleIssueTracker.Issues.Domain",
  values: ["low", "medium", "high", "urgent"],
  names: ["Low", "Medium", "High", "Urgent"],
  isFlags: false,
  default: () => "low",
  test: (v: unknown): v is IssuePriority =>
    v === "low" || v === "medium" || v === "high" || v === "urgent",
};
