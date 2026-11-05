import { SystemException } from "./system-exception.ts";

/** The message `new System.InvalidCastException()` carries on .NET. */
const INVALID_CAST_EXCEPTION_MESSAGE = "Specified cast is not valid.";

/** Mirrors `System.InvalidCastException`: a value that cannot be converted to the requested type. */
export class InvalidCastException extends SystemException {
  override readonly name: string = "InvalidCastException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? INVALID_CAST_EXCEPTION_MESSAGE, innerException);
  }
}
