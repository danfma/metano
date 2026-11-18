import { describe, expect, test } from "bun:test";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { enumFromRaw } from "#/system/reflection/enum-from-raw.ts";
import { enumIsDefined } from "#/system/reflection/enum-is-defined.ts";
import type { EnumType } from "#/system/reflection/runtime-type.ts";

const Color$rt: EnumType<number> = {
  kind: "enum",
  name: "Color",
  fullName: "Geo.Color",
  namespace: "Geo",
  values: [0, 1],
  names: ["Red", "Green"],
  isFlags: false,
  test: (v: unknown): v is number => v === 0 || v === 1,
};

const Perms$rt: EnumType<number> = {
  kind: "enum",
  name: "Perms",
  fullName: "Geo.Perms",
  namespace: "Geo",
  values: [1, 2, 4],
  names: ["Read", "Write", "Execute"],
  isFlags: true,
  test: (v: unknown): v is number => typeof v === "number",
};

const Mode$rt: EnumType<string> = {
  kind: "stringEnum",
  name: "Mode",
  fullName: "Geo.Mode",
  namespace: "Geo",
  values: ["fast", "slow"],
  names: ["Fast", "Slow"],
  isFlags: false,
  test: (v: unknown): v is string => v === "fast" || v === "slow",
};

describe("enumFromRaw with an integral number", () => {
  test("keeps declared and undeclared values and never throws", () => {
    expect(enumFromRaw(Color$rt, 1)).toBe(1);
    expect(enumFromRaw(Color$rt, 99)).toBe(99);
    expect(enumFromRaw(Color$rt, -5)).toBe(-5);
    expect(enumFromRaw(Perms$rt, 7)).toBe(7);
  });

  test("rejects a non-integral number and other types", () => {
    expect(() => enumFromRaw(Color$rt, 1.5)).toThrow(ArgumentException);
    expect(() => enumFromRaw(Color$rt, null)).toThrow(ArgumentException);
    expect(() => enumFromRaw(Color$rt, true)).toThrow(ArgumentException);
  });
});

describe("enumFromRaw with a string", () => {
  test("parses a declared name, case-sensitive by default", () => {
    expect(enumFromRaw(Color$rt, "Green")).toBe(1);
    expect(() => enumFromRaw(Color$rt, "green")).toThrow("Requested value 'green' was not found.");
  });

  test("honors ignoreCase", () => {
    expect(enumFromRaw(Color$rt, "gREEN", true)).toBe(1);
  });

  test("parses a numeric string, undeclared values included", () => {
    expect(enumFromRaw(Color$rt, "99")).toBe(99);
    expect(enumFromRaw(Color$rt, " 1 ")).toBe(1);
    expect(enumFromRaw(Color$rt, "-1")).toBe(-1);
  });

  test("ORs a comma-separated list", () => {
    expect(enumFromRaw(Perms$rt, "Read, Execute")).toBe(5);
    expect(enumFromRaw(Perms$rt, "Read,Write,Execute")).toBe(7);
    expect(enumFromRaw(Perms$rt, "read, WRITE", true)).toBe(3);
    expect(enumFromRaw(Perms$rt, "Read, 8")).toBe(9);
  });

  test("throws ArgumentException for an unknown name", () => {
    expect(() => enumFromRaw(Perms$rt, "Read, Nope")).toThrow(ArgumentException);
    expect(() => enumFromRaw(Perms$rt, "Read, Nope")).toThrow("Requested value 'Nope' was not found.");
    expect(() => enumFromRaw(Color$rt, "")).toThrow(ArgumentException);
  });

  test("maps a string enum's name to its value", () => {
    expect(enumFromRaw(Mode$rt, "Slow")).toBe("slow");
    expect(enumFromRaw(Mode$rt, "slow", true)).toBe("slow");
    expect(() => enumFromRaw(Mode$rt, "Medium")).toThrow("Requested value 'Medium' was not found.");
  });
});

describe("enumIsDefined", () => {
  test("checks declared values", () => {
    expect(enumIsDefined(Color$rt, 1)).toBe(true);
    expect(enumIsDefined(Color$rt, 99)).toBe(false);
    expect(enumIsDefined(Perms$rt, 4)).toBe(true);
    expect(enumIsDefined(Perms$rt, 3)).toBe(false);
  });

  test("checks names for a string", () => {
    expect(enumIsDefined(Color$rt, "Red")).toBe(true);
    expect(enumIsDefined(Color$rt, "red")).toBe(false);
    expect(enumIsDefined(Color$rt, "1")).toBe(false);
  });

  test("accepts a string enum's names and values", () => {
    expect(enumIsDefined(Mode$rt, "Fast")).toBe(true);
    expect(enumIsDefined(Mode$rt, "fast")).toBe(true);
    expect(enumIsDefined(Mode$rt, "medium")).toBe(false);
  });

  test("rejects other types", () => {
    expect(enumIsDefined(Color$rt, null)).toBe(false);
    expect(enumIsDefined(Color$rt, undefined)).toBe(false);
  });
});
