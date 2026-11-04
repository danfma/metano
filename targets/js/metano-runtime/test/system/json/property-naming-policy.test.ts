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
    expect(policy.convert("Item2Count")).toBe("item2_count");
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

// ─── System.Text.Json parity ─────────────────────────────────────────────────
// Measured with System.Text.Json.JsonNamingPolicy.*.ConvertName on .NET 10
// (SDK 10.0.401, runtime 10.0.12). Columns: camelCase, snakeCaseLower,
// snakeCaseUpper, kebabCaseLower, kebabCaseUpper.

const measured: readonly (readonly [string, string, string, string, string, string])[] = [
  ["URL", "url", "url", "URL", "url", "URL"],
  ["IPAddress", "ipAddress", "ip_address", "IP_ADDRESS", "ip-address", "IP-ADDRESS"],
  ["IOStream", "ioStream", "io_stream", "IO_STREAM", "io-stream", "IO-STREAM"],
  ["XMLHttpRequest", "xmlHttpRequest", "xml_http_request", "XML_HTTP_REQUEST", "xml-http-request", "XML-HTTP-REQUEST"],
  ["MyURLValue", "myURLValue", "my_url_value", "MY_URL_VALUE", "my-url-value", "MY-URL-VALUE"],
  ["x", "x", "x", "X", "x", "X"],
  ["_Leading", "_Leading", "_leading", "_LEADING", "_leading", "_LEADING"],
  ["1Abc", "1Abc", "1_abc", "1_ABC", "1-abc", "1-ABC"],
  ["ABc", "aBc", "a_bc", "A_BC", "a-bc", "A-BC"],
  ["AB1", "aB1", "ab1", "AB1", "ab1", "AB1"],
  ["A1B", "a1B", "a1_b", "A1_B", "a1-b", "A1-B"],
  ["ABC_D", "abC_D", "abc_d", "ABC_D", "abc_d", "ABC_D"],
  ["ABCdef1GHI", "abCdef1GHI", "ab_cdef1_ghi", "AB_CDEF1_GHI", "ab-cdef1-ghi", "AB-CDEF1-GHI"],
  ["Item_Name", "item_Name", "item_name", "ITEM_NAME", "item_name", "ITEM_NAME"],
  ["A__B", "a__B", "a__b", "A__B", "a__b", "A__B"],
  ["Value2", "value2", "value2", "VALUE2", "value2", "VALUE2"],
  ["V2Value", "v2Value", "v2_value", "V2_VALUE", "v2-value", "V2-VALUE"],
  ["alreadyCamel", "alreadyCamel", "already_camel", "ALREADY_CAMEL", "already-camel", "ALREADY-CAMEL"],
  ["ÉCole", "éCole", "é_cole", "É_COLE", "é-cole", "É-COLE"],
  ["ΣIGMA", "σigma", "σigma", "ΣIGMA", "σigma", "ΣIGMA"],
  ["İd", "İd", "İd", "İD", "İd", "İD"],
  ["ßTraße", "ßTraße", "ß_traße", "ß_TRAßE", "ß-traße", "ß-TRAßE"],
  ["A٣B", "a٣B", "a٣_b", "A٣_B", "a٣-b", "A٣-B"],
  ["\u{1D400}BC", "\u{1D400}BC", "\u{1D400}bc", "\u{1D400}BC", "\u{1D400}bc", "\u{1D400}BC"],
  ["AB CD", "ab CD", "ab_cd", "AB_CD", "ab-cd", "AB-CD"],
  [" Lead", " Lead", "lead", "LEAD", "lead", "LEAD"],
  ["Trail ", "trail ", "trail", "TRAIL", "trail", "TRAIL"],
  ["A  B", "a  B", "a_b", "A_B", "a-b", "A-B"],
];

describe("PropertyNamingPolicy matches System.Text.Json", () => {
  test.each(measured)("%p", (name, camel, snakeLower, snakeUpper, kebabLower, kebabUpper) => {
    expect(PropertyNamingPolicy.camelCase.convert(name)).toBe(camel);
    expect(PropertyNamingPolicy.snakeCaseLower.convert(name)).toBe(snakeLower);
    expect(PropertyNamingPolicy.snakeCaseUpper.convert(name)).toBe(snakeUpper);
    expect(PropertyNamingPolicy.kebabCaseLower.convert(name)).toBe(kebabLower);
    expect(PropertyNamingPolicy.kebabCaseUpper.convert(name)).toBe(kebabUpper);
  });

  test("ı and ᾀ uppercase one to one", () => {
    expect(PropertyNamingPolicy.snakeCaseUpper.convert("ıd")).toBe("ıD");
    expect(PropertyNamingPolicy.snakeCaseUpper.convert("ᾀb")).toBe("ᾈB");
  });
});
