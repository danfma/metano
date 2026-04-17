import { describe, expect, test } from "bun:test";

import { Priority } from "#/priority";
import { TodoItem } from "#/todo-item";
import { TodoList } from "#/todo-list";

describe("TodoItem", () => {
  test("creates with defaults", () => {
    const item = new TodoItem("Buy milk");
    expect(item.title).toBe("Buy milk");
    expect(item.completed).toBe(false);
    expect(item.priority).toBe(Priority.Medium);
  });

  test("creates with explicit completion", () => {
    const item = new TodoItem("Buy milk", true);
    expect(item.completed).toBe(true);
  });

  test("creates with explicit priority", () => {
    const item = new TodoItem("Buy milk", false, Priority.High);
    expect(item.priority).toBe(Priority.High);
  });

  test("toggleCompleted returns a new instance with completed flipped", () => {
    const item = new TodoItem("Buy milk");
    const toggled = item.toggleCompleted();
    expect(toggled.completed).toBe(true);
    expect(item.completed).toBe(false); // original unchanged
  });

  test("setPriority returns a new instance at the new priority", () => {
    const item = new TodoItem("Buy milk");
    const promoted = item.setPriority(Priority.High);
    expect(promoted.priority).toBe(Priority.High);
    expect(item.priority).toBe(Priority.Medium);
  });

  test("equals matches by structural value", () => {
    const a = new TodoItem("X", false, Priority.Low);
    const b = new TodoItem("X", false, Priority.Low);
    expect(a.equals(b)).toBe(true);
  });

  test("equals rejects different titles", () => {
    const a = new TodoItem("X");
    const b = new TodoItem("Y");
    expect(a.equals(b)).toBe(false);
  });

  test("with overrides only the named field", () => {
    const a = new TodoItem("X", false, Priority.Low);
    const b = a.with({ priority: Priority.High });
    expect(b.title).toBe("X");
    expect(b.completed).toBe(false);
    expect(b.priority).toBe(Priority.High);
  });
});

describe("TodoList", () => {
  test("starts empty", () => {
    const list = new TodoList("Today");
    expect(list.name).toBe("Today");
    expect(list.count).toBe(0);
    expect(list.items).toEqual([]);
  });

  test("Add(title) overload defaults completed to false and priority to medium", () => {
    const list = new TodoList("Today");
    list.add("Buy milk");
    expect(list.count).toBe(1);
    expect(list.items[0]?.title).toBe("Buy milk");
    expect(list.items[0]?.completed).toBe(false);
    expect(list.items[0]?.priority).toBe(Priority.Medium);
  });

  test("Add(title, priority) overload sets the priority", () => {
    const list = new TodoList("Today");
    list.add("Ship feature", Priority.High);
    expect(list.items[0]?.priority).toBe(Priority.High);
  });

  test("Add(item) overload accepts a pre-built TodoItem", () => {
    const list = new TodoList("Today");
    const item = new TodoItem("Buy milk", true, Priority.Low);
    list.add(item);
    expect(list.count).toBe(1);
    expect(list.items[0]).toBe(item);
  });

  test("findByTitle returns the matching item", () => {
    const list = new TodoList("Today");
    list.add("A");
    list.add("B");
    const found = list.findByTitle("B");
    expect(found?.title).toBe("B");
  });

  test("findByTitle returns null when no match", () => {
    const list = new TodoList("Today");
    list.add("A");
    expect(list.findByTitle("missing")).toBe(null);
  });

  test("pendingCount counts only incomplete items", () => {
    const list = new TodoList("Today");
    list.add("A");
    list.add(new TodoItem("B", true));
    list.add("C");
    expect(list.pendingCount).toBe(2);
  });

  test("hasPending returns true when at least one is incomplete", () => {
    const list = new TodoList("Today");
    list.add("A");
    expect(list.hasPending()).toBe(true);
  });

  test("hasPending returns false when all are completed", () => {
    const list = new TodoList("Today");
    list.add(new TodoItem("A", true));
    list.add(new TodoItem("B", true));
    expect(list.hasPending()).toBe(false);
  });

  test("count reflects incremental additions", () => {
    const list = new TodoList("Today");
    expect(list.count).toBe(0);
    list.add("A");
    expect(list.count).toBe(1);
    list.add("B");
    expect(list.count).toBe(2);
  });
});
