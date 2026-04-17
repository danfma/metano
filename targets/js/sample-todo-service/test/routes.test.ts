import { describe, expect, test } from "bun:test";

import type { CreateTodoDto, StoredTodo, UpdateTodoDto } from "#/todos";

// Each test imports the app fresh by cache-busting the dynamic import. The Program
// module instantiates a singleton TodoStore at top level, so without this every test
// would observe state mutations from previous tests.
async function freshApp() {
  const mod = await import(`#/program?bust=${Math.random()}`);
  return mod.default as { fetch: (req: Request) => Promise<Response> };
}

const url = (path: string) => `http://localhost${path}`;

async function postJson<T>(app: Awaited<ReturnType<typeof freshApp>>, path: string, body: unknown): Promise<{ status: number; data: T }> {
  const res = await app.fetch(new Request(url(path), {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  }));
  return { status: res.status, data: await res.json() as T };
}

describe("sample-todo-service routes (cross-package + Hono)", () => {
  test("GET / returns greeting text", async () => {
    const app = await freshApp();
    const res = await app.fetch(new Request(url("/")));
    expect(res.status).toBe(200);
    expect(await res.text()).toBe("sample-todo-service");
  });

  test("GET /todos starts empty", async () => {
    const app = await freshApp();
    const res = await app.fetch(new Request(url("/todos")));
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual([]);
  });

  test("POST /todos creates and returns 201 with the StoredTodo envelope", async () => {
    const app = await freshApp();
    const dto: CreateTodoDto = { title: "Buy milk", priority: "high" };
    const created = await postJson<StoredTodo>(app, "/todos", dto);
    expect(created.status).toBe(201);
    expect(created.data.id).toBeTruthy();
    expect(created.data.item.title).toBe("Buy milk");
    expect(created.data.item.priority).toBe("high");
    expect(created.data.item.completed).toBe(false);
  });

  test("POST then GET round-trips through the in-memory store", async () => {
    const app = await freshApp();

    // Create two
    const a = await postJson<StoredTodo>(app, "/todos", { title: "A", priority: "low" });
    const b = await postJson<StoredTodo>(app, "/todos", { title: "B", priority: "medium" });

    // List should now have both
    const list = await app.fetch(new Request(url("/todos")));
    const items = await list.json() as StoredTodo[];
    expect(items.length).toBe(2);
    const titles = items.map((s) => s.item.title).sort();
    expect(titles).toEqual(["A", "B"]);
    // ids should be unique
    expect(a.data.id).not.toBe(b.data.id);
  });

  test("GET /todos/:id returns the stored todo or 404", async () => {
    const app = await freshApp();
    const created = await postJson<StoredTodo>(app, "/todos", { title: "Find me", priority: "high" });

    const ok = await app.fetch(new Request(url(`/todos/${created.data.id}`)));
    expect(ok.status).toBe(200);
    const body = await ok.json() as StoredTodo;
    expect(body.item.title).toBe("Find me");

    const missing = await app.fetch(new Request(url("/todos/nonexistent-id")));
    expect(missing.status).toBe(404);
  });

  test("PATCH /todos/:id applies partial updates", async () => {
    const app = await freshApp();
    const created = await postJson<StoredTodo>(app, "/todos", { title: "Original", priority: "low" });

    const patch: UpdateTodoDto = { title: "Updated", priority: null, completed: true };
    const res = await app.fetch(new Request(url(`/todos/${created.data.id}`), {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(patch),
    }));
    expect(res.status).toBe(200);
    const updated = await res.json() as StoredTodo;
    expect(updated.id).toBe(created.data.id);
    expect(updated.item.title).toBe("Updated");
    expect(updated.item.completed).toBe(true);
    // priority was null in the patch → unchanged
    expect(updated.item.priority).toBe("low");
  });

  test("PATCH /todos/:id returns 404 for unknown id", async () => {
    const app = await freshApp();
    const res = await app.fetch(new Request(url("/todos/nope"), {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ title: "x", priority: null, completed: null }),
    }));
    expect(res.status).toBe(404);
  });

  test("DELETE /todos/:id removes and returns 204 No Content", async () => {
    const app = await freshApp();
    const created = await postJson<StoredTodo>(app, "/todos", { title: "Doomed", priority: "low" });

    const del = await app.fetch(new Request(url(`/todos/${created.data.id}`), { method: "DELETE" }));
    expect(del.status).toBe(204);
    // 204 has no body — reading text() gives empty string, json() would throw.
    expect(await del.text()).toBe("");

    // Subsequent GET → 404
    const after = await app.fetch(new Request(url(`/todos/${created.data.id}`)));
    expect(after.status).toBe(404);
  });

  test("DELETE /todos/:id returns 404 for unknown id", async () => {
    const app = await freshApp();
    const res = await app.fetch(new Request(url("/todos/missing"), { method: "DELETE" }));
    expect(res.status).toBe(404);
  });
});
