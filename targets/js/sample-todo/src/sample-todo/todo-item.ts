/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, formatBoolean, type ClassType } from "metano-runtime";
import type { Priority } from "./priority";

let TodoItem$rt: ClassType<TodoItem> | undefined;

export class TodoItem {
  constructor(
    readonly title: string,
    readonly completed: boolean = false,
    readonly priority: Priority = "medium",
  ) {}

  static get $runtimeType(): ClassType<TodoItem> {
    return (TodoItem$rt ??= {
      kind: "class",
      name: "TodoItem",
      fullName: "SampleTodo.TodoItem",
      namespace: "SampleTodo",
      ctor: TodoItem,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is TodoItem => v instanceof TodoItem,
    });
  }

  toggleCompleted(): TodoItem {
    return this.with({ completed: !this.completed });
  }

  setPriority(priority: Priority): TodoItem {
    return this.with({ priority: priority });
  }

  toString(): string {
    return `[${this.completed ? "x" : " "}] ${this.title} (${this.priority})`;
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.title === other.title &&
      this.completed === other.completed &&
      this.priority === other.priority
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.title);
    hc.add(this.completed);
    hc.add(this.priority);

    return hc.toHashCode();
  }

  with(overrides?: Partial<TodoItem>): TodoItem {
    return copyRecordWith<TodoItem>(this, overrides);
  }

  protected $printMembers(): string {
    return `Title = ${this.title}, Completed = ${formatBoolean(this.completed)}, Priority = ${this.priority}`;
  }
}
