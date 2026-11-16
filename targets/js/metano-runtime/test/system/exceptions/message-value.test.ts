import { describe, expect, test } from "bun:test";
import { messageValue } from "#/system/exceptions/message-value.ts";

describe("messageValue", () => {
  test("prints a value as JavaScript's String does", () => {
    expect(messageValue(42)).toBe("42");
    expect(messageValue("text")).toBe("text");
    expect(messageValue(true)).toBe("true");
  });

  test("keeps the sign of -0, as .NET prints it", () => {
    expect(messageValue(-0)).toBe("-0");
    expect(messageValue(0)).toBe("0");
  });
});
