/**
 * SQLite bootstrap for the sample. Creates an in-memory database,
 * defines the `products` schema (snake_case columns, mapped one-to-
 * one to the camelCase entity properties), and seeds a small fixture
 * dataset so the provider tests have rows to query against.
 *
 * `bun:sqlite` ships with Bun by default — no extra runtime
 * dependency. The same code would run against a file-backed DB by
 * passing a path instead of `:memory:`.
 */
import { Database } from "bun:sqlite";
import { Decimal } from "decimal.js";
import { Product } from "#/sample-queryable-sqlite/product";

// Loaded eagerly from disk so the bootstrap helper does not need to be
// async. The transpiler copies samples/SampleQueryableSqlite/schema.sql
// to provider/schema.sql via <MetanoAsset> (FR-048).
const SCHEMA_SQL = await Bun.file(new URL("./schema.sql", import.meta.url)).text();

/** Entity-property → SQLite-column name table for the `products` row. */
export const PRODUCT_COLUMN_MAP: Record<string, string> = {
  id: "id",
  name: "name",
  displayName: "display_name",
  unitPrice: "unit_price",
  stockCount: "stock_count",
  isActive: "is_active",
};

/**
 * Resolves an `ExprMember.member` (camelCase TS property) to the
 * snake_case SQLite column. Throws on unknown members so a typo on
 * the entity surface surfaces immediately instead of generating
 * malformed SQL.
 */
export function productColumn(memberName: string): string {
  const column = PRODUCT_COLUMN_MAP[memberName];
  if (column === undefined) {
    throw new Error(`sqlite-sample: no column mapped for Product.${memberName}`);
  }
  return column;
}

/**
 * Reads a raw row coming back from `bun:sqlite` and rebuilds a
 * <see cref="Product"/> instance. Numeric columns are wrapped in
 * `Decimal` because the C# entity declares <c>decimal Price</c>; the
 * column itself is REAL on the SQL side (lossy, suitable for the
 * sample's scale).
 */
export interface ProductRow {
  id: number;
  name: string;
  display_name: string;
  unit_price: number;
  stock_count: number;
  is_active: number;
}

export function mapProductRow(row: ProductRow): Product {
  return new Product(
    row.id,
    row.name,
    row.display_name,
    new Decimal(row.unit_price),
    row.stock_count,
    Boolean(row.is_active),
  );
}

/**
 * Spawns the in-memory schema, applies the seed rows, and returns
 * the live `Database` handle. Callers own the handle and are
 * responsible for closing it (`db.close()`); tests typically open a
 * fresh DB per `describe` block to keep state isolated.
 */
export function createSeededDatabase(): Database {
  const db = new Database(":memory:");
  db.exec(SCHEMA_SQL);
  const insert = db.prepare(
    "INSERT INTO products (id, name, display_name, unit_price, stock_count, is_active) VALUES (?, ?, ?, ?, ?, ?)",
  );
  for (const row of SEED_ROWS) {
    insert.run(
      row.id,
      row.name,
      row.displayName,
      row.unitPrice,
      row.stockCount,
      row.isActive ? 1 : 0,
    );
  }
  return db;
}

/**
 * Seed dataset shared by the bootstrap helper and the tests' golden
 * cross-check. Keeping the data declarative makes the contract
 * obvious from a single read.
 */
export const SEED_ROWS = [
  {
    id: 1,
    name: "alpha",
    displayName: "Alpha Pro",
    unitPrice: 19.99,
    stockCount: 12,
    isActive: true,
  },
  { id: 2, name: "beta", displayName: "Beta Lite", unitPrice: 9.5, stockCount: 0, isActive: true },
  {
    id: 3,
    name: "gamma",
    displayName: "Gamma Max",
    unitPrice: 79.0,
    stockCount: 5,
    isActive: false,
  },
  {
    id: 4,
    name: "delta",
    displayName: "Delta Standard",
    unitPrice: 49.95,
    stockCount: 30,
    isActive: true,
  },
  {
    id: 5,
    name: "epsilon",
    displayName: "Epsilon Mini",
    unitPrice: 4.25,
    stockCount: 100,
    isActive: true,
  },
] as const;
