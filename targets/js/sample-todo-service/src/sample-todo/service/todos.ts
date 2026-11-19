/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { List, type IReadOnlyList } from "metano-runtime/system/collections";
import { UUID } from "metano-runtime";
import { firstOrDefault, linq, orderBy, toList } from "metano-runtime/system/linq";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import { TodoItem, type Priority } from "sample-todo/sample-todo";

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

  return typeof v.title === "string" && v.priority != null;
}

export function assertCreateTodoDto(
  value: unknown,
  message?: string,
): asserts value is CreateTodoDto {
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
  private readonly _items: List<StoredTodo> = List.of([]);

  constructor() {}

  all(): IReadOnlyList<StoredTodo> {
    return linq(
      this._items,
      orderBy((t: StoredTodo) => t.id),
      toList(),
    );
  }

  get(id: string): StoredTodo | null {
    return linq(
      this._items,
      firstOrDefault((t: StoredTodo) => t.id === id),
    );
  }

  add(dto: CreateTodoDto): StoredTodo {
    const id = UUID.newUuid();
    const stored = { id: id, item: new TodoItem(dto.title, false, dto.priority) };
    this._items.add(stored);

    return stored;
  }

  update(id: string, patch: UpdateTodoDto): StoredTodo | null {
    const existing = this._items.find((t: StoredTodo) => t.id === id, null);

    if (existing == null) {
      return null;
    }

    let item = existing.item;

    if (!(patch.title == null)) {
      item = item.with({ title: patch.title });
    }

    if (!(patch.priority == null)) {
      item = item.with({
        priority:
          patch.priority ??
          (() => {
            throw new InvalidOperationException("Nullable object must have a value.");
          })(),
      });
    }

    if (!(patch.completed == null)) {
      item = item.with({
        completed:
          patch.completed ??
          (() => {
            throw new InvalidOperationException("Nullable object must have a value.");
          })(),
      });
    }

    const updated = {
      ...existing,
      item: item,
    };

    this._items.set(this._items.indexOf(existing), updated);

    return updated;
  }

  remove(id: string): boolean {
    const existing = this._items.find((t: StoredTodo) => t.id === id, null);

    if (existing == null) {
      return false;
    }

    this._items.remove(existing);

    return true;
  }
}
