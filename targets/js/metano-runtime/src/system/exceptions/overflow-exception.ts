import { ArithmeticException } from "./arithmetic-exception.ts";

/** The message `new System.OverflowException()` carries on .NET. */
const OVERFLOW_EXCEPTION_MESSAGE = "Arithmetic operation resulted in an overflow.";

/**
 * Mirrors `System.OverflowException`: a checked conversion or operation whose
 * result does not fit its integral type.
 */
export class OverflowException extends ArithmeticException {
  override readonly name: string = "OverflowException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? OVERFLOW_EXCEPTION_MESSAGE, innerException);
  }
}
