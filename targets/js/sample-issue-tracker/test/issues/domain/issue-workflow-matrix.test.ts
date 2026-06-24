import { describe, expect, test } from "bun:test";
import { IssueWorkflow } from "#/sample-issue-tracker/issues/domain/issue-workflow";
import { IssueStatus } from "#/sample-issue-tracker/issues/domain/issue-status";

// Exhaustive from × to matrix. Pins each allowed set against the
// C# authoritative switch in IssueWorkflow.cs. A regression in the
// generated TS (e.g., a missing `or` branch on the discriminated
// switch) shows up here as a specific pair flipping sides.

type TransitionCase = {
  readonly from: IssueStatus;
  readonly allowed: ReadonlySet<IssueStatus>;
};

const ALL_STATUSES: readonly IssueStatus[] = [
  IssueStatus.Backlog,
  IssueStatus.Ready,
  IssueStatus.InProgress,
  IssueStatus.InReview,
  IssueStatus.Done,
  IssueStatus.Cancelled,
];

const CASES: readonly TransitionCase[] = [
  {
    from: IssueStatus.Backlog,
    allowed: new Set([IssueStatus.Ready, IssueStatus.Cancelled]),
  },
  {
    from: IssueStatus.Ready,
    allowed: new Set([IssueStatus.InProgress, IssueStatus.Cancelled]),
  },
  {
    from: IssueStatus.InProgress,
    allowed: new Set([
      IssueStatus.InReview,
      IssueStatus.Backlog,
      IssueStatus.Cancelled,
    ]),
  },
  {
    from: IssueStatus.InReview,
    allowed: new Set([
      IssueStatus.Done,
      IssueStatus.InProgress,
      IssueStatus.Cancelled,
    ]),
  },
  { from: IssueStatus.Done, allowed: new Set() },
  { from: IssueStatus.Cancelled, allowed: new Set() },
];

describe("IssueWorkflow transition matrix", () => {
  for (const { from, allowed } of CASES) {
    describe(`from ${from}`, () => {
      test("getAllowedTransitions returns exactly the expected set", () => {
        const actual = new Set(IssueWorkflow.getAllowedTransitions(from));
        expect(actual.size).toBe(allowed.size);
        for (const s of allowed) {
          expect(actual.has(s)).toBe(true);
        }
      });

      for (const to of ALL_STATUSES) {
        const isAllowed = allowed.has(to);
        test(`canTransition(${from} → ${to}) === ${isAllowed}`, () => {
          expect(IssueWorkflow.canTransition(from, to)).toBe(isAllowed);
        });
      }
    });
  }

  test("terminal states (Done, Cancelled) have no outgoing transitions", () => {
    expect(IssueWorkflow.getAllowedTransitions(IssueStatus.Done)).toEqual([]);
    expect(IssueWorkflow.getAllowedTransitions(IssueStatus.Cancelled)).toEqual(
      [],
    );
  });
});
