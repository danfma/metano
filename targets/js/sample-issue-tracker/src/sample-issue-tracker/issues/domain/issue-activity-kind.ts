/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
export const IssueActivityKind = {
  Created: "created",
  Assigned: "assigned",
  StatusChanged: "status-changed",
  Commented: "commented",
  Closed: "closed",
} as const;

export type IssueActivityKind = (typeof IssueActivityKind)[keyof typeof IssueActivityKind];
