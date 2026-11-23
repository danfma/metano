import {
  ArgumentException,
  ArgumentNullException,
  InvalidOperationException,
  TaskCanceledException,
} from "./exceptions/index.ts";

const ALREADY_COMPLETED_MESSAGE =
  "An attempt was made to transition a task to a final state when it had already completed.";

/**
 * `System.Threading.Tasks.TaskCompletionSource<T>`: a promise (`task`) settled from outside.
 * The first `set*` / `trySet*` call wins; `set*` after that throws `InvalidOperationException`
 * and `trySet*` returns `false`. A faulted or canceled task whose rejection nobody observes
 * does not become an unhandled rejection, as an unobserved .NET task does not crash.
 */
export class TaskCompletionSource<T> {
  readonly task: Promise<T>;
  private resolveTask!: (value: T) => void;
  private rejectTask!: (error: unknown) => void;
  private completed = false;

  constructor() {
    this.task = new Promise<T>((resolve, reject) => {
      this.resolveTask = resolve;
      this.rejectTask = reject;
    });

    this.task.catch(() => undefined);
  }

  /** `TrySetResult(value)`. */
  trySetResult(value: T): boolean {
    return this.complete(() => this.resolveTask(value));
  }

  /** `TrySetException(exception)`. */
  trySetException(exception: Error): boolean {
    return this.complete(() => this.rejectTask(exception));
  }

  /**
   * `TrySetException(IEnumerable<Exception>)`: the task faults with the first exception, which
   * is what awaiting it throws.
   *
   * @throws ArgumentNullException for `null`.
   * @throws ArgumentException for an empty sequence.
   */
  trySetExceptions(exceptions: Iterable<Error> | null): boolean {
    return this.trySetException(firstException(exceptions));
  }

  /** `SetException(IEnumerable<Exception>)`. */
  setExceptions(exceptions: Iterable<Error> | null): void {
    this.setException(firstException(exceptions));
  }

  /** `TrySetCanceled()`. */
  trySetCanceled(): boolean {
    return this.complete(() => this.rejectTask(new TaskCanceledException()));
  }

  /** `SetResult(value)`. */
  setResult(value: T): void {
    this.require(this.trySetResult(value));
  }

  /** `SetException(exception)`. */
  setException(exception: Error): void {
    this.require(this.trySetException(exception));
  }

  /** `SetCanceled()`. */
  setCanceled(): void {
    this.require(this.trySetCanceled());
  }

  private complete(settle: () => void): boolean {
    if (this.completed) {
      return false;
    }

    this.completed = true;
    settle();

    return true;
  }

  private require(succeeded: boolean): void {
    if (!succeeded) {
      throw new InvalidOperationException(ALREADY_COMPLETED_MESSAGE);
    }
  }
}

function firstException(exceptions: Iterable<Error> | null): Error {
  if (exceptions === null) {
    throw new ArgumentNullException("exceptions");
  }

  for (const exception of exceptions) {
    return exception;
  }

  throw new ArgumentException("The exceptions collection was empty.", "exceptions");
}
