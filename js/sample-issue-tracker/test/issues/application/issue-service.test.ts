import { describe, expect, test } from "bun:test";
import { IssueService } from "#/issues/application/issue-service";
import { InMemoryIssueRepository } from "#/issues/application/in-memory-issue-repository";
import { IssueId } from "#/issues/domain/issue-id";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueType } from "#/issues/domain/issue-type";
import { UserId } from "#/shared-kernel/user-id";

describe("IssueService", () => {
  test("createAsync creates and saves a new issue", async () => {
    const repo = new InMemoryIssueRepository();
    const service = new IssueService(repo);
    const result = await service.createAsync("Test", "Desc", IssueType.Story, IssuePriority.Medium);
    expect(result.success).toBe(true);
    expect(result.value?.title).toBe("Test");
    expect(await repo.existsAsync(result.value!.id)).toBe(true);
  });

  test("createAsync with default priority", async () => {
    const repo = new InMemoryIssueRepository();
    const service = new IssueService(repo);
    const result = await service.createAsync("Test", "Desc", IssueType.Bug);
    expect(result.value?.priority).toBe(IssuePriority.Medium);
  });

  test("loadAsync returns failure for missing id", async () => {
    const repo = new InMemoryIssueRepository();
    const service = new IssueService(repo);
    const result = await service.loadAsync(IssueId.new_());
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe("issue_not_found");
  });

  test("assignAsync sets assignee", async () => {
    const repo = new InMemoryIssueRepository();
    const service = new IssueService(repo);
    const created = await service.createAsync("Task", "x", IssueType.Story, IssuePriority.Low);
    const result = await service.assignAsync(created.value!.id, UserId.create("alice"));
    expect(result.value?.assigneeId).toBe("alice" as any);
  });

  test("transitionAsync moves through workflow", async () => {
    const repo = new InMemoryIssueRepository();
    const service = new IssueService(repo);
    const created = await service.createAsync("Task", "x", IssueType.Story, IssuePriority.Low);
    const result = await service.transitionAsync(
      created.value!.id,
      IssueStatus.Ready,
      UserId.create("alice")
    );
    expect(result.value?.status).toBe(IssueStatus.Ready);
  });
});
