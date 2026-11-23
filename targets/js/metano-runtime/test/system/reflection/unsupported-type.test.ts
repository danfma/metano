import { describe, expect, test } from "bun:test";
import { NotSupportedException } from "../../../src/system/exceptions/not-supported-exception.ts";
import { unsupportedType } from "../../../src/system/reflection/unsupported-type.ts";

describe("unsupportedType", () => {
  test("returns false for null and undefined and throws NotSupportedException for non-null values", () => {
    const desc = unsupportedType("System.IDisposable");

    expect(desc.test(null)).toBe(false);
    expect(desc.test(undefined)).toBe(false);
    expect(() => desc.test("x")).toThrow(NotSupportedException);
    expect(() => desc.test(42)).toThrow(NotSupportedException);
  });
});
