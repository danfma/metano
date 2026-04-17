import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Priority, TodoItem } from "sample-todo";
import type { CreateTodoDto, StoredTodo, UpdateTodoDto } from "./todos";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _storedTodo?: TypeSpec<StoredTodo>;

  private _createTodoDto?: TypeSpec<CreateTodoDto>;

  private _updateTodoDto?: TypeSpec<UpdateTodoDto>;

  private _todoItem?: TypeSpec<TodoItem>;

  static get default(): JsonContext {
    return this._default;
  }

  get storedTodo(): TypeSpec<StoredTodo> {
    return this._storedTodo ??= this.createSpec({
      factory: (p: Record<string, unknown>) => ({ id: p.id as string, item: p.item as TodoItem }),
      properties: [
        {
          ts: "id",
          json: "id",
          type: { kind: "primitive" },
        },
        {
          ts: "item",
          json: "item",
          type: {
            kind: "ref",
            spec: () => this.todoItem,
          },
        },
      ],
    });
  }

  get createTodoDto(): TypeSpec<CreateTodoDto> {
    return this._createTodoDto ??= this.createSpec({
      factory: (p: Record<string, unknown>) => ({ title: p.title as string, priority: p.priority as Priority }),
      properties: [
        {
          ts: "title",
          json: "title",
          type: { kind: "primitive" },
        },
        {
          ts: "priority",
          json: "priority",
          type: { kind: "enum", values: Priority },
        },
      ],
    });
  }

  get updateTodoDto(): TypeSpec<UpdateTodoDto> {
    return this._updateTodoDto ??= this.createSpec({
      factory: (p: Record<string, unknown>) => ({ title: p.title as string | null, priority: p.priority as Priority | null, completed: p.completed as boolean | null }),
      properties: [
        {
          ts: "title",
          json: "title",
          type: {
            kind: "nullable",
            inner: { kind: "primitive" },
          },
          optional: true,
        },
        {
          ts: "priority",
          json: "priority",
          type: {
            kind: "nullable",
            inner: { kind: "enum", values: Priority },
          },
          optional: true,
        },
        {
          ts: "completed",
          json: "completed",
          type: {
            kind: "nullable",
            inner: { kind: "primitive" },
          },
          optional: true,
        },
      ],
    });
  }

  get todoItem(): TypeSpec<TodoItem> {
    return this._todoItem ??= this.createSpec({
      type: TodoItem,
      factory: (p: Record<string, unknown>) => new TodoItem(p.title as string, p.completed as boolean, p.priority as Priority),
      properties: [
        {
          ts: "title",
          json: "title",
          type: { kind: "primitive" },
        },
        {
          ts: "completed",
          json: "completed",
          type: { kind: "primitive" },
        },
        {
          ts: "priority",
          json: "priority",
          type: { kind: "enum", values: Priority },
        },
      ],
    });
  }
}
