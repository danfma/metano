import { ArgumentException } from "./argument-exception.ts";

/** The message `new System.ArgumentOutOfRangeException()` carries on .NET. */
const ARGUMENT_OUT_OF_RANGE_EXCEPTION_MESSAGE =
  "Specified argument was out of the range of valid values.";

/**
 * Mirrors `System.ArgumentOutOfRangeException`: an argument outside the values
 * a member accepts. As on .NET, a non-`null` actual value is appended to the
 * message on a new line (`Actual value was <value>.`), formatted with
 * JavaScript's `String`.
 */
export class ArgumentOutOfRangeException extends ArgumentException {
  override readonly name: string = "ArgumentOutOfRangeException";
  readonly actualValue: unknown;

  constructor(
    paramName?: string | null,
    actualValue?: unknown,
    message?: string | null,
    innerException?: Error | null,
  ) {
    super(message ?? ARGUMENT_OUT_OF_RANGE_EXCEPTION_MESSAGE, paramName, innerException);
    this.actualValue = actualValue ?? null;
    // The actual value follows the parameter suffix the base appends, so it
    // can only be added once the base message exists.
    this.message = withActualValue(this.message, actualValue);
  }
}

/** `message` followed by .NET's `\nActual value was <value>.` line when the value is not `null`. */
function withActualValue(message: string, actualValue: unknown): string {
  return actualValue == null ? message : `${message}\nActual value was ${String(actualValue)}.`;
}
