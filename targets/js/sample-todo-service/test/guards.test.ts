import { describe, expect, test } from "bun:test";

import { assertCreateTodoDto, isCreateTodoDto } from "#/todos";

describe("CreateTodoDto guards", () => {
  test("isCreateTodoDto narrows a valid body", () => {
    const body: unknown = { title: "Buy milk", priority: 0 };
    expect(isCreateTodoDto(body)).toBe(true);
  });

  test("isCreateTodoDto rejects a missing title", () => {
    const body: unknown = { priority: 0 };
    expect(isCreateTodoDto(body)).toBe(false);
  });

  test("isCreateTodoDto rejects null and non-objects", () => {
    expect(isCreateTodoDto(null)).toBe(false);
    expect(isCreateTodoDto(undefined)).toBe(false);
    expect(isCreateTodoDto("not an object")).toBe(false);
    expect(isCreateTodoDto(42)).toBe(false);
  });

  test("assertCreateTodoDto passes through a valid body", () => {
    const body: unknown = { title: "Buy milk", priority: 0 };
    expect(() => assertCreateTodoDto(body)).not.toThrow();
  });

  test("assertCreateTodoDto throws TypeError with default message on mismatch", () => {
    const body: unknown = { priority: 0 };
    expect(() => assertCreateTodoDto(body)).toThrow(TypeError);
    expect(() => assertCreateTodoDto(body)).toThrow("Value is not a CreateTodoDto");
  });

  test("assertCreateTodoDto throws with caller-supplied message", () => {
    const body: unknown = { priority: 0 };
    expect(() => assertCreateTodoDto(body, "request body invalid")).toThrow(
      "request body invalid",
    );
  });

  test("assertCreateTodoDto narrows the value after a successful call", () => {
    const body: unknown = { title: "Buy milk", priority: 0 };
    assertCreateTodoDto(body);
    // After the assertion, the compiler narrows body to CreateTodoDto — .title
    // is a string here, no cast needed. Runtime assertion already ran.
    expect(body.title).toBe("Buy milk");
  });
});
