import { Enumerable, UUID, listRemove } from "metano-runtime";
import { TodoItem, type Priority } from "sample-todo";

export interface StoredTodo {
  readonly id: string;
  readonly item: TodoItem;
}

export interface CreateTodoDto {
  readonly title: string;
  readonly priority: Priority;
}

export function isCreateTodoDto(value: unknown): value is CreateTodoDto {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.title === "string" && true;
}

export function assertCreateTodoDto(value: unknown, message?: string): asserts value is CreateTodoDto {
  if (!isCreateTodoDto(value)) {
    throw new TypeError(message ?? "Value is not a CreateTodoDto");
  }
}

export interface UpdateTodoDto {
  readonly title: string | null;
  readonly priority: Priority | null;
  readonly completed: boolean | null;
}

export class TodoStore {
  private readonly _items: StoredTodo[] = [];

  constructor() { }

  all(): StoredTodo[] {
    return Enumerable.from(this._items).orderBy((t: StoredTodo) => t.id).toArray();
  }

  get(id: string): StoredTodo | null {
    return Enumerable.from(this._items).firstOrDefault((t: StoredTodo) => t.id === id);
  }

  add(dto: CreateTodoDto): StoredTodo {
    const id = UUID.newUuid();
    const stored = { id: id, item: new TodoItem(dto.title, false, dto.priority) };
    this._items.push(stored);

    return stored;
  }

  update(id: string, patch: UpdateTodoDto): StoredTodo | null {
    const existing = (this._items.find((t: StoredTodo) => t.id === id) ?? null);

    if (existing == null) {
      return null;
    }

    let item = existing.item;

    if (!(patch.title == null)) {
      item = item.with({ title: patch.title });
    }

    if (!(patch.priority == null)) {
      item = item.with({ priority: patch.priority });
    }

    if (!(patch.completed == null)) {
      item = item.with({ completed: patch.completed });
    }

    const updated = {
      ...existing,
      item: item,
    };

    this._items[this._items.indexOf(existing)] = updated;

    return updated;
  }

  remove(id: string): boolean {
    const existing = (this._items.find((t: StoredTodo) => t.id === id) ?? null);

    if (existing == null) {
      return false;
    }

    listRemove(this._items, existing);

    return true;
  }
}
