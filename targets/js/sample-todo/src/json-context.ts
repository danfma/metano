import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Priority } from "./priority";
import { TodoItem } from "./todo-item";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _todoItem?: TypeSpec<TodoItem>;

  static get default(): JsonContext {
    return this._default;
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
