import { SystemException } from "./system-exception.ts";

/** The message `new System.TimeoutException()` carries on .NET. */
const TIMEOUT_EXCEPTION_MESSAGE = "The operation has timed out.";

/** Mirrors `System.TimeoutException`: an operation that did not finish in its allotted time. */
export class TimeoutException extends SystemException {
  override readonly name: string = "TimeoutException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? TIMEOUT_EXCEPTION_MESSAGE, innerException);
  }
}
