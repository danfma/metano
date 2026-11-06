/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
export const IssueType = {
  Story: "story",
  Bug: "bug",
  Chore: "chore",
  Spike: "spike",
} as const;

export type IssueType = (typeof IssueType)[keyof typeof IssueType];
