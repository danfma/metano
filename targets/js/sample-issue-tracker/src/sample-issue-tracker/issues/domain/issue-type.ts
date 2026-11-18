/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { EnumType } from "metano-runtime";

export const IssueType = {
  Story: "story",
  Bug: "bug",
  Chore: "chore",
  Spike: "spike",
} as const;

export type IssueType = (typeof IssueType)[keyof typeof IssueType];

export const IssueType$runtimeType: EnumType<IssueType> = {
  kind: "stringEnum",
  name: "IssueType",
  fullName: "SampleIssueTracker.Issues.Domain.IssueType",
  namespace: "SampleIssueTracker.Issues.Domain",
  values: ["story", "bug", "chore", "spike"],
  names: ["Story", "Bug", "Chore", "Spike"],
  isFlags: false,
  test: (v: unknown): v is IssueType =>
    v === "story" || v === "bug" || v === "chore" || v === "spike",
};
