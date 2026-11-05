import { SystemException } from "./system-exception.ts";

/** The message `new System.InvalidOperationException()` carries on .NET. */
const INVALID_OPERATION_EXCEPTION_MESSAGE =
  "Operation is not valid due to the current state of the object.";

/** Mirrors `System.InvalidOperationException`: a call that the object's state does not allow. */
export class InvalidOperationException extends SystemException {
  override readonly name: string = "InvalidOperationException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? INVALID_OPERATION_EXCEPTION_MESSAGE, innerException);
  }
}
