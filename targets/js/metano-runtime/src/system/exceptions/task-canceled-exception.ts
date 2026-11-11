import { OperationCanceledException } from "./operation-canceled-exception.ts";

/** The message `new System.Threading.Tasks.TaskCanceledException()` carries on .NET. */
const TASK_CANCELED_EXCEPTION_MESSAGE = "A task was canceled.";

/** Mirrors `System.Threading.Tasks.TaskCanceledException`: a canceled task. */
export class TaskCanceledException extends OperationCanceledException {
  override readonly name: string = "TaskCanceledException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? TASK_CANCELED_EXCEPTION_MESSAGE, innerException);
  }

  protected override get typeNamespace(): string {
    return "System.Threading.Tasks";
  }
}
