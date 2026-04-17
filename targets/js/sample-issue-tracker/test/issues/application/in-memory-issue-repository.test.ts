import { describe, expect, test } from "bun:test";
import { InMemoryIssueRepository } from "#/issues/application/in-memory-issue-repository";
import { IssueId } from "#/issues/domain/issue-id";
import { makeIssue } from "../../helpers";

describe("InMemoryIssueRepository", () => {
  test("save and retrieve by id", async () => {
    const repo = new InMemoryIssueRepository();
    const issue = makeIssue();
    await repo.saveAsync(issue);
    const found = await repo.getByIdAsync(issue.id);
    expect(found).toBe(issue);
  });

  test("getByIdAsync returns null for missing id", async () => {
    const repo = new InMemoryIssueRepository();
    const found = await repo.getByIdAsync(IssueId.new_());
    expect(found).toBeNull();
  });

  test("listAsync returns saved issues", async () => {
    const repo = new InMemoryIssueRepository();
    const a = makeIssue({ title: "First" });
    const b = makeIssue({ title: "Second" });
    await repo.saveAsync(a);
    await repo.saveAsync(b);
    const list = await repo.listAsync();
    expect(list.length).toBe(2);
  });

  test("existsAsync", async () => {
    const repo = new InMemoryIssueRepository();
    const issue = makeIssue();
    expect(await repo.existsAsync(issue.id)).toBe(false);
    await repo.saveAsync(issue);
    expect(await repo.existsAsync(issue.id)).toBe(true);
  });

  test("listBySprintAsync filters by sprint", async () => {
    const repo = new InMemoryIssueRepository();
    const a = makeIssue();
    const b = makeIssue();
    a.planForSprint("SPRINT-1");
    b.planForSprint("SPRINT-2");
    await repo.saveAsync(a);
    await repo.saveAsync(b);
    const result = await repo.listBySprintAsync("SPRINT-1");
    expect(result.length).toBe(1);
    expect(result[0]?.id).toBe(a.id);
  });
});
