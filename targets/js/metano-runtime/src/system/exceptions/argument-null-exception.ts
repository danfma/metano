import { ArgumentException } from "./argument-exception.ts";

/** The message `new System.ArgumentNullException()` carries on .NET. */
const ARGUMENT_NULL_EXCEPTION_MESSAGE = "Value cannot be null.";

/**
 * Mirrors `System.ArgumentNullException`: a `null` argument that a member does
 * not accept. The parameter name comes first, as in .NET's
 * `ArgumentNullException(paramName, message)`.
 */
export class ArgumentNullException extends ArgumentException {
  override readonly name: string = "ArgumentNullException";

  constructor(paramName?: string | null, message?: string | null, innerException?: Error | null) {
    super(message ?? ARGUMENT_NULL_EXCEPTION_MESSAGE, paramName, innerException);
  }
}
