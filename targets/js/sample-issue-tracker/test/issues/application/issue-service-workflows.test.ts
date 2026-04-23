import { describe, expect, test } from "bun:test";
import { IssueService } from "#/issues/application/issue-service";
import { InMemoryIssueRepository } from "#/issues/application/in-memory-issue-repository";
import { IssueId } from "#/issues/domain/issue-id";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueType } from "#/issues/domain/issue-type";
import { UserId } from "#/shared-kernel/user-id";
import { PageRequest } from "#/shared-kernel/page-request";

// End-to-end workflows that exercise chained service operations +
// OperationResult error propagation. Existing issue-service.test.ts
// spot-checks individual calls; these cover the plumbing between them.

function buildService() {
  const repo = new InMemoryIssueRepository();
  return { repo, service: new IssueService(repo) };
}

describe("IssueService workflows", () => {
  test("create → assign → planSprint → transition reaches Ready", async () => {
    const { service } = buildService();
    const created = await service.createAsync(
      "End-to-end",
      "flow",
      IssueType.Story,
      IssuePriority.High,
    );
    expect(created.success).toBe(true);
    const id = created.value!.id;

    const assigned = await service.assignAsync(id, UserId.create("alice"));
    expect(assigned.value?.assigneeId).toBe("alice" as any);

    const planned = await service.planSprintAsync(id, "SPRINT-1");
    expect(planned.value?.sprintKey).toBe("SPRINT-1");

    const transitioned = await service.transitionAsync(
      id,
      IssueStatus.Ready,
      UserId.create("alice"),
    );
    expect(transitioned.value?.status).toBe(IssueStatus.Ready);
    expect(transitioned.value?.id).toBe(id);
  });

  test("assignAsync propagates not_found when issue missing", async () => {
    const { service } = buildService();
    const result = await service.assignAsync(
      IssueId.new_(),
      UserId.create("alice"),
    );
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe("issue_not_found");
  });

  test("planSprintAsync propagates not_found when issue missing", async () => {
    const { service } = buildService();
    const result = await service.planSprintAsync(IssueId.new_(), "SPRINT-1");
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe("issue_not_found");
  });

  test("transitionAsync propagates not_found when issue missing", async () => {
    const { service } = buildService();
    const result = await service.transitionAsync(
      IssueId.new_(),
      IssueStatus.Ready,
      UserId.create("alice"),
    );
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe("issue_not_found");
  });

  test("addCommentAsync on existing issue returns ok with accumulated comment", async () => {
    const { service } = buildService();
    const created = await service.createAsync(
      "Issue",
      "desc",
      IssueType.Story,
      IssuePriority.Medium,
    );
    const id = created.value!.id;
    const withComment = await service.addCommentAsync(
      id,
      UserId.create("reviewer"),
      "Looks good",
    );
    expect(withComment.success).toBe(true);
    expect(withComment.value?.commentCount).toBe(1);
    expect(withComment.value?.comments[0]?.message).toBe("Looks good");
  });

  test("addCommentAsync propagates not_found when issue missing", async () => {
    const { service } = buildService();
    const result = await service.addCommentAsync(
      IssueId.new_(),
      UserId.create("alice"),
      "orphan",
    );
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe("issue_not_found");
  });

  test("searchAsync honors status + priority filters", async () => {
    const { service } = buildService();
    await service.createAsync("Low", "x", IssueType.Bug, IssuePriority.Low);
    const high = await service.createAsync(
      "High",
      "x",
      IssueType.Bug,
      IssuePriority.High,
    );
    await service.transitionAsync(
      high.value!.id,
      IssueStatus.Ready,
      UserId.create("alice"),
    );

    const readyHigh = await service.searchAsync(
      IssueStatus.Ready,
      IssuePriority.High,
      null,
      null,
      new PageRequest(0, 10),
    );
    expect(readyHigh.items.length).toBe(1);
    expect(readyHigh.items[0]?.priority).toBe(IssuePriority.High);
    expect(readyHigh.items[0]?.status).toBe(IssueStatus.Ready);
  });

  test("invalid status transition surfaces as a thrown Error", async () => {
    const { service } = buildService();
    const created = await service.createAsync(
      "Task",
      "x",
      IssueType.Story,
      IssuePriority.Medium,
    );
    // Backlog → Done is disallowed; the underlying Issue.transitionTo
    // throws (the C# `InvalidOperationException` lowers to a plain JS
    // `Error`) and the async wrapper propagates the rejection.
    // OperationResult shape doesn't swallow runtime exceptions today —
    // this test pins that expectation so a future change to the
    // dispatcher surfaces. The message format is part of the contract
    // so assert against it instead of just `toThrow()`.
    await expect(
      service.transitionAsync(
        created.value!.id,
        IssueStatus.Done,
        UserId.create("alice"),
      ),
    ).rejects.toThrow(/Cannot transition issue from/);
  });
});
