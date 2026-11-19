import { describe, expect, test } from "bun:test";

import {
  ArrayType,
  DelegateType,
  ObjectType,
  UnknownType,
} from "#/system/reflection/well-known-types.ts";

describe("well-known descriptors", () => {
  test("UnknownType accepts everything", () => {
    expect([null, undefined, 1, "a", {}].every((value) => UnknownType.test(value))).toBe(true);
  });

  test("ObjectType accepts everything but null and undefined", () => {
    expect(ObjectType.fullName).toBe("System.Object");
    expect(ObjectType.test(0)).toBe(true);
    expect(ObjectType.test(null)).toBe(false);
    expect(ObjectType.test(undefined)).toBe(false);
  });

  test("ArrayType accepts arrays only", () => {
    expect(ArrayType.name).toBe("Array");
    expect(ArrayType.fullName).toBe("System.Array");
    expect(ArrayType.test([])).toBe(true);
    expect(ArrayType.test({ length: 0 })).toBe(false);
  });

  test("DelegateType accepts functions only", () => {
    expect(DelegateType.fullName).toBe("System.Delegate");
    expect(DelegateType.test(() => 1)).toBe(true);
    expect(DelegateType.test({})).toBe(false);
  });
});
