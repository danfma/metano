/**
 * End-to-end coverage for the C# `ProductDemo` / `IProductRepository`
 * pair backed by the TS-side `BunSqliteProductRepository`.
 *
 * Each test:
 *  1. Calls a generated C# entry (`ProductDemo.activeProducts`, etc.)
 *  2. Lets the repository inspect the chain via `getStages` (#200),
 *     translate it to SQL, and execute against `bun:sqlite`.
 *  3. Cross-checks the materialized result against an in-memory
 *     filter over the same seed rows so the SQL semantics stay
 *     aligned with what the closure path would have produced.
 */
import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import type { Database } from "bun:sqlite";
import { Decimal } from "decimal.js";
import { BunSqliteProductRepository } from "#/sample-queryable-sqlite/provider/bun-sqlite-product-repository.ts";
import { createSeededDatabase, SEED_ROWS } from "#/sample-queryable-sqlite/provider/db.ts";
import { Product } from "#/sample-queryable-sqlite/product.ts";
import { ProductDemo } from "#/sample-queryable-sqlite/provider/product-demo.ts";

let db: Database;
let repo: BunSqliteProductRepository;

beforeAll(() => {
  db = createSeededDatabase();
  repo = new BunSqliteProductRepository(db);
});

afterAll(() => {
  db.close();
});

describe("ProductDemo via BunSqliteProductRepository", () => {
  test("ActiveProducts — translates Where(p => p.IsActive) to SQL", () => {
    const result = ProductDemo.activeProducts(repo);
    const expected = SEED_ROWS.filter((r) => r.isActive).map((r) => r.id);
    expect(result.map((p) => p.id)).toEqual(expected);
  });

  test("InStock — translates composite predicate to AND clause", () => {
    const result = ProductDemo.inStock(repo);
    const expected = SEED_ROWS.filter((r) => r.isActive && r.stockCount > 0).map((r) => r.id);
    expect(result.map((p) => p.id)).toEqual(expected);
  });

  test("AtLeastPrice — captured local resolves through positional parameter", () => {
    const result = ProductDemo.atLeastPrice(repo, new Decimal(20));
    const expected = SEED_ROWS.filter((r) => r.unitPrice >= 20).map((r) => r.id);
    expect(result.map((p) => p.id)).toEqual(expected);
  });

  test("TopByPrice — orderByDescending + take maps to ORDER BY DESC LIMIT", () => {
    const result = ProductDemo.topByPrice(repo, 2);
    const expected = [...SEED_ROWS]
      .sort((a, b) => b.unitPrice - a.unitPrice)
      .slice(0, 2)
      .map((r) => r.id);
    expect(result.map((p) => p.id)).toEqual(expected);
  });

  test("Page — orderBy + skip + take maps to LIMIT/OFFSET", () => {
    const result = ProductDemo.page(repo, 1, 2);
    expect(result.map((p) => p.id)).toEqual([2, 3]);
  });

  test("CountActive — terminal collapses to SELECT COUNT(*)", () => {
    const total = ProductDemo.countActive(repo);
    expect(total).toBe(SEED_ROWS.filter((r) => r.isActive).length);
  });

  test("FirstById — orderBy + first appends LIMIT 1", () => {
    const product = ProductDemo.firstById(repo);
    expect(product).not.toBeNull();
    expect((product as Product).id).toBe(1);
  });
});
