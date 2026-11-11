import { SystemException } from "./system-exception.ts";

/** The message `new System.MissingMethodException()` carries on .NET. */
const MISSING_METHOD_EXCEPTION_MESSAGE = "Attempted to access a missing method.";

/** Mirrors `System.MissingMethodException`: a member that does not exist at run time.
 * .NET derives it from `MissingMemberException` and `MemberAccessException`,
 * which the runtime does not define, so it extends `SystemException` directly. */
export class MissingMethodException extends SystemException {
  override readonly name: string = "MissingMethodException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? MISSING_METHOD_EXCEPTION_MESSAGE, innerException);
  }
}
