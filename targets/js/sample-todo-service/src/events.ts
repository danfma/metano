export const TodoEventKind = {
  TodoCreated: "TodoCreated",
  TodoUpdated: "TodoUpdated",
  TodoDeleted: "TodoDeleted",
} as const;

export type TodoEventKind = typeof TodoEventKind[keyof typeof TodoEventKind];

export interface TodoCreated {
  readonly kind: TodoEventKind;
  readonly id: string;
  readonly title: string;
}

export function isTodoCreated(value: unknown): value is TodoCreated {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  if (v.kind !== "TodoCreated") {
    return false;
  }

  return typeof v.id === "string" && typeof v.title === "string";
}

export function assertTodoCreated(value: unknown, message?: string): asserts value is TodoCreated {
  if (!isTodoCreated(value)) {
    throw new TypeError(message ?? "Value is not a TodoCreated");
  }
}

export interface TodoUpdated {
  readonly kind: TodoEventKind;
  readonly id: string;
  readonly title: string | null;
  readonly completed: boolean | null;
}

export function isTodoUpdated(value: unknown): value is TodoUpdated {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  if (v.kind !== "TodoUpdated") {
    return false;
  }

  return typeof v.id === "string" && (v.title == null || typeof v.title === "string") && (v.completed == null || typeof v.completed === "boolean");
}

export function assertTodoUpdated(value: unknown, message?: string): asserts value is TodoUpdated {
  if (!isTodoUpdated(value)) {
    throw new TypeError(message ?? "Value is not a TodoUpdated");
  }
}
