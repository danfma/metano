import { describe, expect, test } from "bun:test";
import { HashCode } from "../hash-code.ts";
import { HashSet } from "./hash-set.ts";

// Mock value object with equals/hashCode
class UserId {
  constructor(readonly value: string) {}
  equals(other: any): boolean {
    return other instanceof UserId && this.value === other.value;
  }
  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);
    return hc.toHashCode();
  }
}

describe("HashSet", () => {
  // ─── Primitives ────────────────────────────────────────

  test("add/has/delete with numbers", () => {
    const set = new HashSet<number>();
    set.add(1);
    set.add(2);
    set.add(3);
    expect(set.size).toBe(3);
    expect(set.has(2)).toBe(true);
    expect(set.has(4)).toBe(false);
    set.delete(2);
    expect(set.size).toBe(2);
    expect(set.has(2)).toBe(false);
  });

  test("add/has/delete with strings", () => {
    const set = new HashSet<string>();
    set.add("a");
    set.add("b");
    expect(set.size).toBe(2);
    expect(set.has("a")).toBe(true);
    expect(set.has("c")).toBe(false);
  });

  test("deduplicates primitives", () => {
    const set = new HashSet<number>();
    set.add(1);
    set.add(1);
    set.add(1);
    expect(set.size).toBe(1);
  });

  // ─── Value objects ─────────────────────────────────────

  test("deduplicates by equals/hashCode", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("abc"));
    set.add(new UserId("abc")); // same value, different reference
    set.add(new UserId("def"));
    expect(set.size).toBe(2);
  });

  test("has() uses equals for lookup", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("abc"));
    expect(set.has(new UserId("abc"))).toBe(true); // different ref, same value
    expect(set.has(new UserId("xyz"))).toBe(false);
  });

  test("delete() uses equals for lookup", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("abc"));
    expect(set.delete(new UserId("abc"))).toBe(true); // different ref
    expect(set.size).toBe(0);
    expect(set.delete(new UserId("abc"))).toBe(false); // already gone
  });

  // ─── Iteration ─────────────────────────────────────────

  test("iterates all items", () => {
    const set = new HashSet<number>();
    set.add(1);
    set.add(2);
    set.add(3);
    const items = [...set];
    expect(items.sort()).toEqual([1, 2, 3]);
  });

  test("iterates value objects", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("a"));
    set.add(new UserId("b"));
    const values = [...set].map(u => u.value).sort();
    expect(values).toEqual(["a", "b"]);
  });

  test("forEach iterates all items", () => {
    const set = new HashSet<number>();
    set.add(10);
    set.add(20);
    const result: number[] = [];
    set.forEach(n => result.push(n));
    expect(result.sort()).toEqual([10, 20]);
  });

  test("values() returns iterator", () => {
    const set = new HashSet<string>();
    set.add("x");
    set.add("y");
    const iter = set.values();
    const items: string[] = [];
    for (const v of iter) items.push(v);
    expect(items.sort()).toEqual(["x", "y"]);
  });

  // ─── Other operations ──────────────────────────────────

  test("clear() removes all items", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("a"));
    set.add(new UserId("b"));
    set.clear();
    expect(set.size).toBe(0);
    expect(set.has(new UserId("a"))).toBe(false);
  });

  test("constructor with iterable", () => {
    const set = new HashSet([1, 2, 3, 2, 1]);
    expect(set.size).toBe(3);
  });

  test("toArray()", () => {
    const set = new HashSet<number>();
    set.add(5);
    set.add(10);
    expect(set.toArray().sort((a, b) => a - b)).toEqual([5, 10]);
  });

  test("mixed primitives and iteration", () => {
    const set = new HashSet<number | string>();
    set.add(1);
    set.add("one");
    set.add(1);
    set.add("one");
    expect(set.size).toBe(2);
  });
});
