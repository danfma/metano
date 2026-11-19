import { describe, expect, test } from "bun:test";
import { Exception, FormatException, namedException } from "#/system/exceptions/index.ts";

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
