import { SystemException } from "./system-exception.ts";

/** The message `new System.NotSupportedException()` carries on .NET. */
const NOT_SUPPORTED_EXCEPTION_MESSAGE = "Specified method is not supported.";

/** Mirrors `System.NotSupportedException`: an operation the runtime does not implement. */
export class NotSupportedException extends SystemException {
  override readonly name: string = "NotSupportedException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? NOT_SUPPORTED_EXCEPTION_MESSAGE, innerException);
  }
}
