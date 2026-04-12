import { isString } from "metano-runtime";
import { Enumerable } from "metano-runtime";
import { Priority } from "./priority";
import { TodoItem } from "./todo-item";

export class TodoList {
  private readonly _items: TodoItem[] = [];

  constructor(readonly name: string) { }

  get items(): TodoItem[] {
    return this._items;
  }

  get count(): number {
    return this._items.length;
  }

  get pendingCount(): number {
    return Enumerable.from(this._items).count((i: TodoItem) => !i.completed);
  }

  private addTitlePriority(title: string, priority: Priority): void {
    this._items.push(new TodoItem(title, false, priority));
  }

  private addItem(item: TodoItem): void {
    this._items.push(item);
  }

  private addTitle(title: string): void {
    this._items.push(new TodoItem(title));
  }

  add(title: string, priority: Priority): void;
  add(item: TodoItem): void;
  add(title: string): void;
  add(...args: unknown[]): void {
    if (args.length === 2 && isString(args[0]) && (args[1] === "low" || args[1] === "medium" || args[1] === "high")) {
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
    return Enumerable.from(this._items).firstOrDefault((i: TodoItem) => i.title === title);
  }

  hasPending(): boolean {
    return Enumerable.from(this._items).any((i: TodoItem) => !i.completed);
  }
}
