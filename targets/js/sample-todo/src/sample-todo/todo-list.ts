/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isString, isType, type ClassType } from "metano-runtime";
import { any, count, firstOrDefault, linq } from "metano-runtime/system/linq";
import { Priority$runtimeType, type Priority } from "./priority";
import { TodoItem } from "./todo-item";

let TodoList$rt: ClassType<TodoList> | undefined;

export class TodoList {
  private readonly _items: TodoItem[] = [];

  constructor(readonly name: string) {}

  static get $runtimeType(): ClassType<TodoList> {
    return (TodoList$rt ??= {
      kind: "class",
      name: "TodoList",
      fullName: "SampleTodo.TodoList",
      namespace: "SampleTodo",
      ctor: TodoList,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is TodoList => v instanceof TodoList,
    });
  }

  get items(): TodoItem[] {
    return this._items;
  }

  get count(): number {
    return this._items.length;
  }

  get pendingCount(): number {
    return linq(
      this._items,
      count((i: TodoItem) => !i.completed),
    );
  }

  addTitlePriority(title: string, priority: Priority): void {
    this._items.push(new TodoItem(title, false, priority));
  }

  addItem(item: TodoItem): void {
    this._items.push(item);
  }

  addTitle(title: string): void {
    this._items.push(new TodoItem(title));
  }

  add(title: string, priority: Priority): void;
  add(item: TodoItem): void;
  add(title: string): void;
  add(...args: unknown[]): void {
    if (args.length === 2 && isString(args[0]) && isType(args[1], Priority$runtimeType)) {
      this.addTitlePriority(args[0] as string, args[1] as Priority);

      return;
    }

    if (args.length === 1 && args[0] instanceof TodoItem) {
      this.addItem(args[0] as TodoItem);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      this.addTitle(args[0] as string);

      return;
    }

    throw new Error("No matching overload for add");
  }

  findByTitle(title: string): TodoItem | null {
    return linq(
      this._items,
      firstOrDefault((i: TodoItem) => i.title === title),
    );
  }

  hasPending(): boolean {
    return linq(
      this._items,
      any((i: TodoItem) => !i.completed),
    );
  }
}
