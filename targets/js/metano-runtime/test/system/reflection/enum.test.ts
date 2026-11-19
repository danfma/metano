import { describe, expect, test } from "bun:test";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { OverflowException } from "#/system/exceptions/overflow-exception.ts";
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
    expect(() => enumFromRaw(Color$rt, 1.5)).toThrow(
      "The value passed in must be an enum base or an underlying type for an enum, such as an Int32. (Parameter 'value')",
    );
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
  });

  test("accepts a number only as the whole text, not inside a list", () => {
    expect(enumFromRaw(Perms$rt, "+5")).toBe(5);
    expect(() => enumFromRaw(Perms$rt, "Read, 8")).toThrow("Requested value 'Read, 8' was not found.");
    expect(() => enumFromRaw(Perms$rt, "1, 2")).toThrow("Requested value '1, 2' was not found.");
  });

  test("throws ArgumentException for an unknown name", () => {
    expect(() => enumFromRaw(Perms$rt, "Read, Nope")).toThrow(ArgumentException);
    expect(() => enumFromRaw(Perms$rt, "Read, Nope")).toThrow("Requested value 'Read, Nope' was not found.");
    expect(() => enumFromRaw(Color$rt, "   ")).toThrow(
      "Must specify valid information for parsing in the string. (Parameter 'value')",
    );
  });

  test("maps a string enum's name to its value", () => {
    expect(enumFromRaw(Mode$rt, "Slow")).toBe("slow");
    expect(enumFromRaw(Mode$rt, "slow", true)).toBe("slow");
    expect(() => enumFromRaw(Mode$rt, "Medium")).toThrow("Requested value 'Medium' was not found.");
  });
});

const Tiny$rt: EnumType<number> = {
  kind: "enum",
  name: "Tiny",
  fullName: "Geo.Tiny",
  namespace: "Geo",
  values: [1],
  names: ["One"],
  isFlags: false,
  underlying: "Byte",
  test: (v: unknown): v is number => typeof v === "number",
};

const Big$rt: EnumType<number> = {
  kind: "enum",
  name: "Big",
  fullName: "Geo.Big",
  namespace: "Geo",
  values: [1, 3000000000],
  names: ["A", "B"],
  isFlags: false,
  underlying: "UInt32",
  test: (v: unknown): v is number => typeof v === "number",
};

describe("enumFromRaw in the underlying type's width", () => {
  test("ToObject wraps an integer, a bigint included", () => {
    expect(enumFromRaw(Tiny$rt, 300)).toBe(44);
    expect(enumFromRaw(Color$rt, 1n)).toBe(1);
  });

  test("Parse rejects a number outside the range", () => {
    expect(() => enumFromRaw(Tiny$rt, "300")).toThrow(OverflowException);
    expect(() => enumFromRaw(Tiny$rt, "300")).toThrow(
      "Value was either too large or too small for an unsigned byte.",
    );
    expect(() => enumFromRaw(Perms$rt, "99999999999")).toThrow(
      "Value was either too large or too small for an Int32.",
    );
  });

  test("an unsigned enum keeps its values above the Int32 range", () => {
    expect(enumFromRaw(Big$rt, "3000000000")).toBe(3000000000);
    expect(enumFromRaw(Big$rt, "A, B")).toBe(3000000001);
  });

  test("a string enum maps a number through its members' positions", () => {
    expect(enumFromRaw(Mode$rt, 1)).toBe("slow");
    expect(enumFromRaw(Mode$rt, "1")).toBe("slow");
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

/** A `ulong` enum with no member (#701): its values are `bigint`s all the same. */
const Empty64$rt: EnumType<bigint> = {
  kind: "enum",
  name: "Empty64",
  fullName: "Geo.Empty64",
  namespace: "Geo",
  values: [],
  names: [],
  isFlags: false,
  underlying: "UInt64",
  test: (v: unknown): v is bigint => typeof v === "bigint",
};

const Wide$rt: EnumType<bigint> = {
  kind: "enum",
  name: "Wide",
  fullName: "Geo.Wide",
  namespace: "Geo",
  values: [1n, 1099511627776n],
  names: ["Low", "High"],
  isFlags: true,
  underlying: "Int64",
  test: (v: unknown): v is bigint => typeof v === "bigint",
};

describe("enumFromRaw over a 64-bit underlying type (#701)", () => {
  test("returns the exact bigint, decided by the underlying type", () => {
    expect(enumFromRaw(Empty64$rt, 5)).toBe(5n);
    expect(enumFromRaw(Empty64$rt, -1n)).toBe(18446744073709551615n);
    expect(enumFromRaw(Empty64$rt, "9223372036854775809")).toBe(9223372036854775809n);
    expect(enumFromRaw(Wide$rt, "Low, High")).toBe(1099511627777n);
  });

  test("IsDefined compares bigint members", () => {
    expect(enumIsDefined(Wide$rt, 1099511627776n)).toBe(true);
    expect(enumIsDefined(Wide$rt, 1099511627777n)).toBe(false);
  });
});
