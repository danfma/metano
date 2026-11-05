import { SystemException } from "./system-exception.ts";

/** The message `new System.ArithmeticException()` carries on .NET. */
const ARITHMETIC_EXCEPTION_MESSAGE = "Overflow or underflow in the arithmetic operation.";

/** Mirrors `System.ArithmeticException`, the base of overflow and division errors. */
export class ArithmeticException extends SystemException {
  override readonly name: string = "ArithmeticException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? ARITHMETIC_EXCEPTION_MESSAGE, innerException);
  }
}
