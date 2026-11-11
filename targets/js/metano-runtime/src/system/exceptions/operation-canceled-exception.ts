import { SystemException } from "./system-exception.ts";

/** The message `new System.OperationCanceledException()` carries on .NET. */
const OPERATION_CANCELED_EXCEPTION_MESSAGE = "The operation was canceled.";

/** Mirrors `System.OperationCanceledException`: an operation stopped by its cancellation. */
export class OperationCanceledException extends SystemException {
  override readonly name: string = "OperationCanceledException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? OPERATION_CANCELED_EXCEPTION_MESSAGE, innerException);
  }
}
