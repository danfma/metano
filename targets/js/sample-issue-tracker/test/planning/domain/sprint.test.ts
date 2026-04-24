import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
import { Sprint } from "#/planning/domain/sprint";
import { IssueId } from "#/issues/domain/issue-id";

function d(iso: string): Temporal.PlainDate {
  return Temporal.PlainDate.from(iso);
}

describe("Sprint", () => {
  test("durationDays is inclusive of both endpoints", () => {
    const sprint = new Sprint(
      "SPRINT-1",
      "Iteration 1",
      d("2026-01-01"),
      d("2026-01-14"),
    );
    expect(sprint.durationDays).toBe(14);
  });

  test("durationDays is 1 when start equals end", () => {
    const sprint = new Sprint(
      "SPRINT-0",
      "Spike",
      d("2026-01-01"),
      d("2026-01-01"),
    );
    expect(sprint.durationDays).toBe(1);
  });

  test("isActiveOn includes start and end dates", () => {
    const sprint = new Sprint(
      "SPRINT-1",
      "Iteration 1",
      d("2026-01-05"),
      d("2026-01-10"),
    );
    expect(sprint.isActiveOn(d("2026-01-05"))).toBe(true);
    expect(sprint.isActiveOn(d("2026-01-07"))).toBe(true);
    expect(sprint.isActiveOn(d("2026-01-10"))).toBe(true);
    expect(sprint.isActiveOn(d("2026-01-04"))).toBe(false);
    expect(sprint.isActiveOn(d("2026-01-11"))).toBe(false);
  });

  test("plan adds a unique IssueId", () => {
    const sprint = new Sprint(
      "SPRINT-1",
      "Iteration",
      d("2026-01-01"),
      d("2026-01-14"),
    );
    const a = IssueId.create("issue-a");
    const b = IssueId.create("issue-b");

    sprint.plan(a);
    sprint.plan(b);
    expect(sprint.plannedCount).toBe(2);
  });

  test("plan deduplicates when the same IssueId is added twice", () => {
    const sprint = new Sprint(
      "SPRINT-1",
      "Iteration",
      d("2026-01-01"),
      d("2026-01-14"),
    );
    const a = IssueId.create("issue-a");
    sprint.plan(a);
    sprint.plan(a);
    expect(sprint.plannedCount).toBe(1);
  });

  test("unplan removes a specific IssueId", () => {
    const sprint = new Sprint(
      "SPRINT-1",
      "Iteration",
      d("2026-01-01"),
      d("2026-01-14"),
    );
    const a = IssueId.create("issue-a");
    const b = IssueId.create("issue-b");
    sprint.plan(a);
    sprint.plan(b);
    sprint.unplan(a);
    expect(sprint.plannedCount).toBe(1);
    const planned = Array.from(sprint.plannedIssues);
    expect(planned).toContain(b);
    expect(planned).not.toContain(a);
  });

  test("rename updates the Name but leaves Key", () => {
    const sprint = new Sprint(
      "SPRINT-1",
      "Old",
      d("2026-01-01"),
      d("2026-01-14"),
    );
    sprint.rename("New");
    expect(sprint.name).toBe("New");
    expect(sprint.key).toBe("SPRINT-1");
  });

  test("reschedule updates both boundaries", () => {
    const sprint = new Sprint(
      "SPRINT-1",
      "Iteration",
      d("2026-01-01"),
      d("2026-01-14"),
    );
    sprint.reschedule(d("2026-02-01"), d("2026-02-28"));
    expect(sprint.startDate.toString()).toBe("2026-02-01");
    expect(sprint.endDate.toString()).toBe("2026-02-28");
    expect(sprint.durationDays).toBe(28);
  });
});
