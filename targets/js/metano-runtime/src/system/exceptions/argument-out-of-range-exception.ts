import { ArgumentException } from "./argument-exception.ts";
import { messageValue } from "./message-value.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.ArgumentOutOfRangeException()` carries on .NET. */
const ARGUMENT_OUT_OF_RANGE_EXCEPTION_MESSAGE =
  "Specified argument was out of the range of valid values.";

let ArgumentOutOfRangeException$rt: ClassType<ArgumentOutOfRangeException> | undefined;

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

  /**
   * The runtime type descriptor of `System.ArgumentOutOfRangeException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<ArgumentOutOfRangeException> {
    return (ArgumentOutOfRangeException$rt ??= {
      kind: "class",
      name: "ArgumentOutOfRangeException",
      fullName: "System.ArgumentOutOfRangeException",
      namespace: "System",
      ctor: ArgumentOutOfRangeException,
      base: () => ArgumentException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new ArgumentOutOfRangeException(),
      test: (value: unknown): value is ArgumentOutOfRangeException =>
        value instanceof ArgumentOutOfRangeException,
    });
  }
}

/**
 * `message` followed by .NET's `\nActual value was <value>.` line when the value is not
 * `null`, formatted by {@link messageValue}.
 */
function withActualValue(message: string, actualValue: unknown): string {
  return actualValue == null
    ? message
    : `${message}\nActual value was ${messageValue(actualValue)}.`;
}
