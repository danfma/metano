import { describe, expect, test } from "bun:test";
import { PageRequest } from "#/shared-kernel/page-request";
import { PageResult } from "#/shared-kernel/page-result";

describe("PageResult", () => {
  test("constructor stores items and metadata", () => {
    const items = [1, 2, 3];
    const page = new PageRequest(1, 10);
    const result = new PageResult(items, 50, page);
    expect(result.items).toBe(items);
    expect(result.totalCount).toBe(50);
    expect(result.page).toBe(page);
  });
});
