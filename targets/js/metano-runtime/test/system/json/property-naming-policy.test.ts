import { describe, expect, test } from "bun:test";
import { PropertyNamingPolicy } from "#/system/json/property-naming-policy.ts";

// ─── CamelCase ───────────────────────────────────────────────────────────────

describe("PropertyNamingPolicy.camelCase", () => {
  const policy = PropertyNamingPolicy.camelCase;

  test("simple PascalCase", () => {
    expect(policy.convert("FirstName")).toBe("firstName");
  });

  test("single word", () => {
    expect(policy.convert("Name")).toBe("name");
  });

  test("already camelCase", () => {
    expect(policy.convert("firstName")).toBe("firstName");
  });

  test("acronym at start", () => {
    expect(policy.convert("HTMLParser")).toBe("htmlParser");
  });

  test("all uppercase short", () => {
    expect(policy.convert("ID")).toBe("id");
  });

  test("single char", () => {
    expect(policy.convert("X")).toBe("x");
  });

  test("empty string", () => {
    expect(policy.convert("")).toBe("");
  });

  test("multi-word", () => {
    expect(policy.convert("CurrentStatus")).toBe("currentStatus");
  });
});

// ─── SnakeCaseLower ──────────────────────────────────────────────────────────

describe("PropertyNamingPolicy.snakeCaseLower", () => {
  const policy = PropertyNamingPolicy.snakeCaseLower;

  test("simple PascalCase", () => {
    expect(policy.convert("FirstName")).toBe("first_name");
  });

  test("single word", () => {
    expect(policy.convert("Name")).toBe("name");
  });

  test("multi-word", () => {
    expect(policy.convert("CurrentStatus")).toBe("current_status");
  });

  test("acronym", () => {
    expect(policy.convert("HTMLParser")).toBe("html_parser");
  });

  test("with numbers", () => {
    expect(policy.convert("Item2Count")).toBe("item_2_count");
  });

  test("all uppercase", () => {
    expect(policy.convert("ID")).toBe("id");
  });
});

// ─── SnakeCaseUpper ──────────────────────────────────────────────────────────

describe("PropertyNamingPolicy.snakeCaseUpper", () => {
  const policy = PropertyNamingPolicy.snakeCaseUpper;

  test("simple PascalCase", () => {
    expect(policy.convert("FirstName")).toBe("FIRST_NAME");
  });

  test("multi-word", () => {
    expect(policy.convert("CurrentStatus")).toBe("CURRENT_STATUS");
  });
});

// ─── KebabCaseLower ──────────────────────────────────────────────────────────

describe("PropertyNamingPolicy.kebabCaseLower", () => {
  const policy = PropertyNamingPolicy.kebabCaseLower;

  test("simple PascalCase", () => {
    expect(policy.convert("FirstName")).toBe("first-name");
  });

  test("acronym", () => {
    expect(policy.convert("HTMLParser")).toBe("html-parser");
  });
});

// ─── KebabCaseUpper ──────────────────────────────────────────────────────────

describe("PropertyNamingPolicy.kebabCaseUpper", () => {
  const policy = PropertyNamingPolicy.kebabCaseUpper;

  test("simple PascalCase", () => {
    expect(policy.convert("FirstName")).toBe("FIRST-NAME");
  });
});
