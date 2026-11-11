import { InvalidOperationException } from "./invalid-operation-exception.ts";

/** The message of `System.Runtime.CompilerServices.SwitchExpressionException` on .NET. */
const SWITCH_EXPRESSION_EXCEPTION_MESSAGE =
  "Non-exhaustive switch expression failed to match its input.";

/**
 * Mirrors `System.Runtime.CompilerServices.SwitchExpressionException`: a switch
 * expression no arm matched. The unmatched value comes first, as in .NET's
 * `SwitchExpressionException(unmatchedValue)` and like the other value-carrying
 * classes (`ArgumentOutOfRangeException`); when it is not `null` the message
 * names it on a new line as .NET does (`Unmatched value was 5.`, formatted with
 * JavaScript's `String`).
 */
export class SwitchExpressionException extends InvalidOperationException {
  override readonly name: string = "SwitchExpressionException";
  readonly unmatchedValue: unknown;

  constructor(unmatchedValue?: unknown, message?: string | null, innerException?: Error | null) {
    super(
      withUnmatchedValue(message ?? SWITCH_EXPRESSION_EXCEPTION_MESSAGE, unmatchedValue),
      innerException,
    );
    this.unmatchedValue = unmatchedValue ?? null;
  }

  protected override get $typeNamespace(): string {
    return "System.Runtime.CompilerServices";
  }
}

/** `message` followed by .NET's `\nUnmatched value was <value>.` line when the value is not `null`. */
function withUnmatchedValue(message: string, unmatchedValue: unknown): string {
  return unmatchedValue == null
    ? message
    : `${message}\nUnmatched value was ${String(unmatchedValue)}.`;
}
