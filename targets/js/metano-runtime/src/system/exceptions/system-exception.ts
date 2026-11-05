import { Exception } from "./exception.ts";

/** The message `new System.SystemException()` carries on .NET. */
const SYSTEM_EXCEPTION_MESSAGE = "System error.";

/** Mirrors `System.SystemException`, the base of the exceptions the runtime raises itself. */
export class SystemException extends Exception {
  override readonly name: string = "SystemException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? SYSTEM_EXCEPTION_MESSAGE, innerException);
  }
}
