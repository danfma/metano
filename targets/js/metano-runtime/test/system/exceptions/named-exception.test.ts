import { describe, expect, test } from "bun:test";
import {
  Exception,
  FormatException,
  InvalidOperationException,
  namedException,
  SwitchExpressionException,
  SystemException,
  TimeoutException,
} from "#/system/exceptions/index.ts";

describe("namedException", () => {
  test("keeps the message and reports the C# type like .NET's ToString", () => {
    const inner = new FormatException("i");
    const exception = namedException("System.IO", "IOException", "late", inner);

    expect(exception).toBeInstanceOf(Exception);
    expect(exception.message).toBe("late");
    expect(exception.name).toBe("IOException");
    expect(exception.innerException).toBe(inner);
    expect(exception.toString()).toBe(
      "System.IO.IOException: late\n ---> System.FormatException: i\n   --- End of inner exception stack trace ---",
    );
  });
});

describe("TimeoutException and SwitchExpressionException", () => {
  test("TimeoutException is a SystemException with .NET's default message", () => {
    const exception = new TimeoutException();

    expect(exception).toBeInstanceOf(SystemException);
    expect(exception.toString()).toBe("System.TimeoutException: The operation has timed out.");
  });

  test("SwitchExpressionException names the unmatched value as .NET does", () => {
    const exception = new SwitchExpressionException(undefined, undefined, 5);

    expect(exception).toBeInstanceOf(InvalidOperationException);
    expect(exception.message).toBe(
      "Non-exhaustive switch expression failed to match its input.\nUnmatched value was 5.",
    );
    expect(exception.unmatchedValue).toBe(5);
    expect(new SwitchExpressionException(undefined, undefined, null).message).toBe(
      "Non-exhaustive switch expression failed to match its input.",
    );
    expect(exception.toString()).toStartWith(
      "System.Runtime.CompilerServices.SwitchExpressionException: ",
    );
  });
});
