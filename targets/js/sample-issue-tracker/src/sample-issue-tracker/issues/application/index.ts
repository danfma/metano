/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export type { IIssueRepository } from "./i-issue-repository";
export { InMemoryIssueRepository } from "./in-memory-issue-repository";
export { issuesForAssignee, openIssues, readyForReview, statusCounts } from "./issue-queries";
export { IssueService } from "./issue-service";
