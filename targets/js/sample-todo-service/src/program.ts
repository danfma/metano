import { Hono } from "hono";
import { TodoStore } from "./todos";

const app = new Hono();
const store = new TodoStore();

app.get("/", (c) => c.text("sample-todo-service"));

app.get("/todos", (c) => c.json(store.all()));

app.get("/todos/:id", (c) => {
  const id = c.req.param("id");

  if (id == null) {
    return c.notFound();
  }

  const stored = store.get(id);

  return stored == null ? c.notFound() : c.json(stored);
});

app.post("/todos", async (c) => {
  const dto = await c.req.json();
  const created = store.add(dto);

  return c.json(created, 201);
});

app.patch("/todos/:id", async (c) => {
  const id = c.req.param("id");

  if (id == null) {
    return c.notFound();
  }

  const patch = await c.req.json();
  const updated = store.update(id, patch);

  return updated == null ? c.notFound() : c.json(updated);
});

app.delete("/todos/:id", (c) => {
  const id = c.req.param("id");

  if (id == null) {
    return c.notFound();
  }

  return store.remove(id) ? c.body(null, 204) : c.notFound();
});

export default app;
