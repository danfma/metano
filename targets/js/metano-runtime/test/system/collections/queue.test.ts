import {
  describe,
  expect,
  test,
} from "bun:test";
import { Queue } from "#/system/collections/queue.ts";
import { InvalidOperationException } from "#/system/exceptions/invalid-operation-exception.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

function thrown(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("Queue", () => {
  test("first in, first out across the ring's wrap and growth", () => {
    const queue = new Queue<number>();

    for (let i = 0; i < 10; i++) queue.enqueue(i);
    for (let i = 0; i < 7; i++) expect(queue.dequeue()).toBe(i);
    for (let i = 10; i < 20; i++) queue.enqueue(i);

    expect(queue.count).toBe(13);
    expect(queue.peek()).toBe(7);
    expect([...queue]).toEqual([7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19]);
    expect(queue.toArray()).toEqual([...queue]);
  });

  test("interleaved enqueue / dequeue (a breadth-first walk) stays amortized O(1)", () => {
    const queue = Queue.from([0]);
    const started = performance.now();
    let expected = 0;
    let operations = 0;

    while (operations < 200_000) {
      const item = queue.dequeue();

      expect(item).toBe(expected++);
      queue.enqueue(2 * item + 1);
      queue.enqueue(2 * item + 2);
      operations += 3;
    }

    expect(queue.count).toBe(operations / 3 + 1);
    expect(performance.now() - started).toBeLessThan(2_000);
  });

  test("empty dequeue / peek throw; try members return the default", () => {
    const queue = new Queue<number>();
    const error = thrown(() => queue.dequeue());

    expect(error).toBeInstanceOf(InvalidOperationException);
    expect(error.message).toBe("Queue empty.");
    expect(thrown(() => queue.peek()).message).toBe("Queue empty.");
    expect(queue.tryDequeue(0)).toEqual([false, 0]);
    expect(queue.tryPeek(0)).toEqual([false, 0]);

    queue.enqueue(4);

    expect(queue.tryPeek(0)).toEqual([true, 4]);
    expect(queue.tryDequeue(0)).toEqual([true, 4]);
  });

  test("from enqueues in order; contains, clear, copyTo, JSON, toString", () => {
    const queue = Queue.from([1, 2, 3], [Int32Type]);
    const destination = [0, 0, 0];

    queue.copyTo(destination);

    expect(destination).toEqual([1, 2, 3]);
    expect(queue.contains(2)).toBe(true);
    expect(JSON.stringify(queue)).toBe("[1,2,3]");
    expect(queue.toString()).toBe("System.Collections.Generic.Queue`1[System.Int32]");
    queue.clear();
    expect(queue.count).toBe(0);
    expect(thrown(() => Queue.from(null as never)).message).toBe(
      "Value cannot be null. (Parameter 'collection')",
    );
  });

  test("enqueue, dequeue and clear invalidate; peek does not", () => {
    for (const mutate of [
      (q: Queue<number>) => q.enqueue(0),
      (q: Queue<number>) => q.dequeue(),
      (q: Queue<number>) => q.clear(),
    ]) {
      const queue = Queue.from([1, 2]);

      expect(thrown(() => {
        for (const _ of queue) mutate(queue);
      }).message).toBe("Collection was modified; enumeration operation may not execute.");
    }

    const queue = Queue.from([1, 2]);

    expect(() => {
      for (const _ of queue) queue.peek();
    }).not.toThrow();
  });
});
