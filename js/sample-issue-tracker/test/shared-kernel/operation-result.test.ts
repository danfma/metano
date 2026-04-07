import { describe, expect, test } from "bun:test";
import { OperationResult } from "#/shared-kernel/operation-result";

describe("OperationResult", () => {
  test("ok() creates successful result with value", () => {
    const result = OperationResult.ok(42);
    expect(result.success).toBe(true);
    expect(result.value).toBe(42);
    expect(result.hasValue).toBe(true);
  });

  test("fail() creates failure with error info", () => {
    const result = OperationResult.fail<number>("invalid", "Bad input");
    expect(result.success).toBe(false);
    expect(result.value).toBeNull();
    expect(result.errorCode).toBe("invalid");
    expect(result.errorMessage).toBe("Bad input");
    expect(result.hasValue).toBe(false);
  });

  test("equals() compares structurally", () => {
    const a = OperationResult.ok("hello");
    const b = OperationResult.ok("hello");
    expect(a.equals(b)).toBe(true);
  });

  test("with() creates a modified copy", () => {
    const a = OperationResult.ok(1);
    const b = a.with({ value: 2 });
    expect(a.value).toBe(1);
    expect(b.value).toBe(2);
    expect(b.success).toBe(true);
  });
});
