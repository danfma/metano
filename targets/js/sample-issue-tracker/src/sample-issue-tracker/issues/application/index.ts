/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
export type { IIssueRepository } from "./i-issue-repository";
export { InMemoryIssueRepository } from "./in-memory-issue-repository";
export { IssueActivityLog } from "./issue-activity-log";
export { issuesForAssignee, openIssues, readyForReview, statusCounts } from "./issue-queries";
export { IssueService } from "./issue-service";
