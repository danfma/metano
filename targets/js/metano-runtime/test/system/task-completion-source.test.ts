import { describe, expect, test } from "bun:test";
import { ArgumentException, InvalidOperationException, TaskCanceledException } from "#/system/exceptions/index.ts";
import { TaskCompletionSource } from "#/system/task-completion-source.ts";

describe("TaskCompletionSource", () => {
  test("settles the task with the result", async () => {
    const source = new TaskCompletionSource<number>();

    source.setResult(3);

    expect(await source.task).toBe(3);
  });

  test("faults the task with the exception", async () => {
    const source = new TaskCompletionSource<number>();

    source.setException(new Error("boom"));

    await expect(source.task).rejects.toThrow("boom");
  });

  test("cancels the task", async () => {
    const source = new TaskCompletionSource<number>();

    source.setCanceled();

    await expect(source.task).rejects.toBeInstanceOf(TaskCanceledException);
  });

  test("keeps the first outcome: trySet returns false, set throws", async () => {
    const source = new TaskCompletionSource<number>();

    expect(source.trySetResult(1)).toBe(true);
    expect(source.trySetResult(2)).toBe(false);
    expect(source.trySetCanceled()).toBe(false);
    expect(() => source.setResult(2)).toThrow(InvalidOperationException);
    expect(await source.task).toBe(1);
  });

  test("does not raise an unhandled rejection for a faulted task nobody awaits", async () => {
    const source = new TaskCompletionSource<number>();

    source.setException(new Error("ignored"));
    await new Promise((resolve) => setTimeout(resolve, 0));
  });

  test("faults the task with the first of several exceptions", async () => {
    const source = new TaskCompletionSource<number>();

    source.setExceptions([new Error("a"), new Error("b")]);

    await expect(source.task).rejects.toThrow("a");
  });

  test("rejects an empty sequence of exceptions", () => {
    const source = new TaskCompletionSource<number>();

    expect(() => source.setExceptions([])).toThrow(ArgumentException);
    expect(() => source.trySetExceptions(null)).toThrow();
  });
});
