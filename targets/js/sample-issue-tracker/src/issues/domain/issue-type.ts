/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export const IssueType = {
  Story: "story",
  Bug: "bug",
  Chore: "chore",
  Spike: "spike",
} as const;

export type IssueType = typeof IssueType[keyof typeof IssueType];
