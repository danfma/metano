import { describe, expect, test } from "bun:test";
import { PageRequest } from "#/shared-kernel/page-request";

describe("PageRequest", () => {
  test("default values are 1 and 20", () => {
    const req = new PageRequest();
    expect(req.number).toBe(1);
    expect(req.size).toBe(20);
  });

  test("safeNumber clamps to minimum 1", () => {
    const req = new PageRequest(0, 10);
    expect(req.safeNumber).toBe(1);
  });

  test("safeSize clamps to minimum 1", () => {
    const req = new PageRequest(1, 0);
    expect(req.safeSize).toBe(1);
  });

  test("skip computes offset correctly", () => {
    expect(new PageRequest(1, 20).skip).toBe(0);
    expect(new PageRequest(2, 20).skip).toBe(20);
    expect(new PageRequest(3, 10).skip).toBe(20);
  });
});
