/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Priority } from "./priority";
import { TodoItem } from "./todo-item";

let JsonContext$rt: ClassType<JsonContext> | undefined;

export class JsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new JsonContext() as JsonContext;
  private _todoItem?: TypeSpec<TodoItem>;

  static get $runtimeType(): ClassType<JsonContext> {
    return (JsonContext$rt ??= {
      kind: "class",
      name: "JsonContext",
      fullName: "SampleTodo.JsonContext",
      namespace: "SampleTodo",
      ctor: JsonContext,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new JsonContext(),
      test: (v: unknown): v is JsonContext => v instanceof JsonContext,
    });
  }

  static get default(): JsonContext {
    return JsonContext._default;
  }

  get todoItem(): TypeSpec<TodoItem> {
    return (this._todoItem ??= this.createSpec({
      type: TodoItem,
      runtimeType: () => TodoItem.$runtimeType,
      factory: (p: Record<string, unknown>) =>
        new TodoItem(p.title as string, p.completed as boolean, p.priority as Priority),
      properties: [
        {
          ts: "title",
          json: "title",
          type: { kind: "primitive" },
        },
        {
          ts: "completed",
          json: "completed",
          type: { kind: "primitive", typeName: "System.Boolean" },
        },
        {
          ts: "priority",
          json: "priority",
          type: { kind: "enum", values: Priority, typeName: "SampleTodo.Priority" },
        },
      ],
    }));
  }
}
