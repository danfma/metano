import { SystemException } from "./system-exception.ts";

/** The message `new System.NullReferenceException()` carries on .NET. */
const NULL_REFERENCE_EXCEPTION_MESSAGE = "Object reference not set to an instance of an object.";

/** Mirrors `System.NullReferenceException`: a member access on `null`. */
export class NullReferenceException extends SystemException {
  override readonly name: string = "NullReferenceException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? NULL_REFERENCE_EXCEPTION_MESSAGE, innerException);
  }
}
