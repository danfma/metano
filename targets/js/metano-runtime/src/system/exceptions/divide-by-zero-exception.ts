import { ArithmeticException } from "./arithmetic-exception.ts";

/** The message `new System.DivideByZeroException()` carries on .NET. */
const DIVIDE_BY_ZERO_EXCEPTION_MESSAGE = "Attempted to divide by zero.";

/** Mirrors `System.DivideByZeroException`: an integral or decimal division by zero. */
export class DivideByZeroException extends ArithmeticException {
  override readonly name: string = "DivideByZeroException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? DIVIDE_BY_ZERO_EXCEPTION_MESSAGE, innerException);
  }
}
