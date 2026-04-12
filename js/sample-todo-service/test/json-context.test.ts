import { describe, expect, test } from "bun:test";

import { JsonSerializer } from "metano-runtime/system/json";
import { Priority, TodoItem } from "sample-todo";
import { JsonContext } from "#/json-context";
import type { CreateTodoDto, StoredTodo, UpdateTodoDto } from "#/todos";

const ctx = JsonContext.default;

// ─── StoredTodo (PlainObject with nested cross-package TodoItem) ─────────────

describe("StoredTodo serialization", () => {
  const stored: StoredTodo = {
    id: "abc-123",
    item: new TodoItem("Buy milk", false, Priority.High),
  };

  test("serializes to camelCase JSON with nested TodoItem", () => {
    const json = JsonSerializer.serialize(stored, ctx.storedTodo);

    expect(json.id).toBe("abc-123");
    expect(json.item).toBeDefined();

    const item = json.item as Record<string, unknown>;
    expect(item.title).toBe("Buy milk");
    expect(item.completed).toBe(false);
    expect(item.priority).toBe(Priority.High);
  });

  test("round-trips through JSON.stringify + parse", () => {
    const json = JsonSerializer.serialize(stored, ctx.storedTodo);
    const wire = JSON.stringify(json);
    const parsed = JSON.parse(wire);
    const result = JsonSerializer.deserialize<StoredTodo>(parsed, ctx.storedTodo);

    expect(result.id).toBe("abc-123");
    expect(result.item).toBeInstanceOf(TodoItem);
    expect(result.item.title).toBe("Buy milk");
    expect(result.item.completed).toBe(false);
    expect(result.item.priority).toBe(Priority.High);
  });

  test("deserializes a raw JSON payload (HTTP boundary simulation)", () => {
    const payload = {
      id: "from-wire",
      item: { title: "Walk the dog", completed: true, priority: "low" },
    };

    const result = JsonSerializer.deserialize<StoredTodo>(payload, ctx.storedTodo);

    expect(result.id).toBe("from-wire");
    expect(result.item).toBeInstanceOf(TodoItem);
    expect(result.item.title).toBe("Walk the dog");
    expect(result.item.completed).toBe(true);
    expect(result.item.priority).toBe(Priority.Low);
  });
});

// ─── CreateTodoDto (PlainObject — simple DTO) ───────────────────────────────

describe("CreateTodoDto serialization", () => {
  const dto: CreateTodoDto = { title: "Read a book", priority: Priority.Medium };

  test("serializes with camelCase keys", () => {
    const json = JsonSerializer.serialize(dto, ctx.createTodoDto);

    expect(json.title).toBe("Read a book");
    expect(json.priority).toBe("medium");
  });

  test("round-trips as a plain object (no class instance)", () => {
    const json = JsonSerializer.serialize(dto, ctx.createTodoDto);
    const wire = JSON.stringify(json);
    const parsed = JSON.parse(wire);
    const result = JsonSerializer.deserialize<CreateTodoDto>(parsed, ctx.createTodoDto);

    expect(result.title).toBe("Read a book");
    expect(result.priority).toBe(Priority.Medium);
  });
});

// ─── UpdateTodoDto (PlainObject with nullable fields) ────────────────────────

describe("UpdateTodoDto serialization", () => {
  test("serializes with all fields present", () => {
    const dto: UpdateTodoDto = {
      title: "Updated title",
      priority: Priority.Low,
      completed: true,
    };
    const json = JsonSerializer.serialize(dto, ctx.updateTodoDto);

    expect(json.title).toBe("Updated title");
    expect(json.priority).toBe("low");
    expect(json.completed).toBe(true);
  });

  test("serializes null fields as null", () => {
    const dto: UpdateTodoDto = { title: null, priority: null, completed: null };
    const json = JsonSerializer.serialize(dto, ctx.updateTodoDto);

    expect(json.title).toBeNull();
    expect(json.priority).toBeNull();
    expect(json.completed).toBeNull();
  });

  test("deserializes a partial update payload from the wire", () => {
    const payload = { title: "New name", priority: null, completed: null };
    const result = JsonSerializer.deserialize<UpdateTodoDto>(
      payload,
      ctx.updateTodoDto,
    );

    expect(result.title).toBe("New name");
    expect(result.priority).toBeNull();
    expect(result.completed).toBeNull();
  });
});

// ─── Hono boundary simulation ────────────────────────────────────────────────

describe("Hono request/response boundary", () => {
  test("POST /todos → serialize the created StoredTodo for the response body", () => {
    // Simulate what the Hono route does: receive a CreateTodoDto, create a
    // StoredTodo, and serialize it for the JSON response.
    const requestBody: CreateTodoDto = {
      title: "Deploy v1",
      priority: Priority.High,
    };
    const created: StoredTodo = {
      id: "generated-id",
      item: new TodoItem(requestBody.title, false, requestBody.priority),
    };

    const responseJson = JsonSerializer.serialize(created, ctx.storedTodo);
    const wire = JSON.stringify(responseJson);

    // The client receives this JSON and parses it:
    const clientParsed = JSON.parse(wire);
    expect(clientParsed.id).toBe("generated-id");
    expect(clientParsed.item.title).toBe("Deploy v1");
    expect(clientParsed.item.priority).toBe("high");
    expect(clientParsed.item.completed).toBe(false);
  });

  test("PATCH /todos/:id → deserialize UpdateTodoDto from request, apply, re-serialize", () => {
    // Existing stored todo:
    const existing: StoredTodo = {
      id: "todo-1",
      item: new TodoItem("Original", false, Priority.Medium),
    };

    // Incoming PATCH body from client:
    const patchWire = '{"title":"Patched","priority":null,"completed":true}';
    const patch = JsonSerializer.deserialize<UpdateTodoDto>(
      JSON.parse(patchWire),
      ctx.updateTodoDto,
    );

    // Apply the patch (mimicking TodoStore.update logic):
    let item = existing.item;
    if (patch.title !== null) item = item.with({ title: patch.title });
    if (patch.priority !== null) item = item.with({ priority: patch.priority });
    if (patch.completed !== null) item = item.with({ completed: patch.completed });
    const updated: StoredTodo = { ...existing, item };

    // Serialize back for the response:
    const responseJson = JsonSerializer.serialize(updated, ctx.storedTodo);

    expect(responseJson.id).toBe("todo-1");
    const responseItem = responseJson.item as Record<string, unknown>;
    expect(responseItem.title).toBe("Patched");
    expect(responseItem.priority).toBe("medium"); // unchanged
    expect(responseItem.completed).toBe(true); // patched
  });
});
