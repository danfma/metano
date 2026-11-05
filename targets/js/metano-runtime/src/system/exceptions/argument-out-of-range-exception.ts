import { ArgumentException } from "./argument-exception.ts";

/** The message `new System.ArgumentOutOfRangeException()` carries on .NET. */
const ARGUMENT_OUT_OF_RANGE_EXCEPTION_MESSAGE =
  "Specified argument was out of the range of valid values.";

/** Mirrors `System.ArgumentOutOfRangeException`: an argument outside the values a member accepts. */
export class ArgumentOutOfRangeException extends ArgumentException {
  override readonly name: string = "ArgumentOutOfRangeException";
  readonly actualValue: unknown;

  constructor(paramName?: string | null, actualValue?: unknown, message?: string | null) {
    super(message ?? ARGUMENT_OUT_OF_RANGE_EXCEPTION_MESSAGE, paramName);
    this.actualValue = actualValue ?? null;
  }
}
