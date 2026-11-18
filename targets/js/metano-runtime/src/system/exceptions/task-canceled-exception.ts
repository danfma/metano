import { OperationCanceledException } from "./operation-canceled-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.Threading.Tasks.TaskCanceledException()` carries on .NET. */
const TASK_CANCELED_EXCEPTION_MESSAGE = "A task was canceled.";

let TaskCanceledException$rt: ClassType<TaskCanceledException> | undefined;

/** Mirrors `System.Threading.Tasks.TaskCanceledException`: a canceled task. */
export class TaskCanceledException extends OperationCanceledException {
  override readonly name: string = "TaskCanceledException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? TASK_CANCELED_EXCEPTION_MESSAGE, innerException);
  }

  protected override get $typeNamespace(): string {
    return "System.Threading.Tasks";
  }

  /**
   * The runtime type descriptor of `System.Threading.Tasks.TaskCanceledException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<TaskCanceledException> {
    return (TaskCanceledException$rt ??= {
      kind: "class",
      name: "TaskCanceledException",
      fullName: "System.Threading.Tasks.TaskCanceledException",
      namespace: "System.Threading.Tasks",
      ctor: TaskCanceledException,
      base: () => OperationCanceledException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new TaskCanceledException(),
      test: (value: unknown): value is TaskCanceledException =>
        value instanceof TaskCanceledException,
    });
  }
}
