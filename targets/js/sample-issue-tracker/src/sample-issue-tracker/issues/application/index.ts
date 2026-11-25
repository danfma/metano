/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
export { IIssueRepository$runtimeType } from "./i-issue-repository.js";
export type { IIssueRepository } from "./i-issue-repository.js";
export { InMemoryIssueRepository } from "./in-memory-issue-repository.js";
export { IssueActivityLog } from "./issue-activity-log.js";
export { issuesForAssignee, openIssues, readyForReview, statusCounts } from "./issue-queries.js";
export { IssueService } from "./issue-service.js";
