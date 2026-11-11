import { SystemException } from "./system-exception.ts";

/** The message `new System.NotImplementedException()` carries on .NET. */
const NOT_IMPLEMENTED_EXCEPTION_MESSAGE = "The method or operation is not implemented.";

/** Mirrors `System.NotImplementedException`: a member that has no implementation yet. */
export class NotImplementedException extends SystemException {
  override readonly name: string = "NotImplementedException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? NOT_IMPLEMENTED_EXCEPTION_MESSAGE, innerException);
  }
}
