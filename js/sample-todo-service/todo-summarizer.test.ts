import { describe, expect, test } from "bun:test";

import { TodoItem, Priority } from "sample-todo";
import { TodoSummarizer } from "#/todo-summarizer";

describe("TodoSummarizer (cross-package)", () => {
  test("describes a pending item with brackets", () => {
    const item = new TodoItem("Buy milk");
    const summary = new TodoSummarizer().describe(item);
    expect(summary).toBe("[ ] Buy milk (medium)");
  });

  test("describes a completed item with x", () => {
    const item = new TodoItem("Buy milk", true);
    const summary = new TodoSummarizer().describe(item);
    expect(summary).toBe("[x] Buy milk (medium)");
  });

  test("withHighPriority returns a new item at high priority", () => {
    const item = new TodoItem("Ship feature");
    const promoted = new TodoSummarizer().withHighPriority(item);
    expect(promoted.priority).toBe(Priority.High);
    expect(item.priority).toBe(Priority.Medium); // original unchanged
  });
});
