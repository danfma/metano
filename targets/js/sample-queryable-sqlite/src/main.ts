/**
 * Entry point for `bun run .` — wires the hand-written
 * <see cref="BunSqliteProductRepository"/> into the C#-emitted
 * <see cref="ProductDemo"/> static class so the user can see the
 * end-to-end flow on the terminal:
 *
 *  1. Bootstrap an in-memory SQLite database with the seeded
 *     `products` table.
 *  2. Construct the repository (acts as the adapter implementation
 *     for the transpiled `IProductRepository` contract).
 *  3. Run a handful of demo queries via the C# entry points; the
 *     repository reads each chain's descriptor list (Phase B trees +
 *     LINQ_STAGES, #200), translates it to SQL, and prints both the
 *     query inputs and the materialized result.
 *
 * Run from this package's directory:
 *
 *   bun run .
 */
import { Decimal } from "decimal.js";
import { BunSqliteProductRepository } from "#/provider/bun-sqlite-product-repository";
import { createSeededDatabase } from "#/provider/db";
import type { Product } from "#/product";
import { ProductDemo } from "#/provider/product-demo";

const db = createSeededDatabase();
const repo = new BunSqliteProductRepository(db, (sql, params) => {
  const renderedParams = params.length === 0 ? "" : `  -- params: ${JSON.stringify(params)}`;
  console.log(`  SQL > ${sql}${renderedParams}`);
});

try {
  printSection("All active products", ProductDemo.activeProducts(repo));
  printSection("Active and in stock", ProductDemo.inStock(repo));
  printSection("At least $20.00", ProductDemo.atLeastPrice(repo, new Decimal(20)));
  printSection("Top 2 by price (desc)", ProductDemo.topByPrice(repo, 2));
  printSection("Page 2 (skip=1, take=2) by id", ProductDemo.page(repo, 1, 2));
  console.log("\n• countActive →", ProductDemo.countActive(repo));
  console.log("• firstById   →", formatProduct(ProductDemo.firstById(repo)));
} finally {
  db.close();
}

function printSection(title: string, products: Product[]): void {
  console.log(`\n=== ${title} (${products.length} row${products.length === 1 ? "" : "s"})`);
  for (const product of products) console.log("  -", formatProduct(product));
}

function formatProduct(product: Product | null): string {
  if (product === null) return "<none>";
  return (
    `#${product.id} ${product.displayName.padEnd(16)} ` +
    `$${product.unitPrice.toFixed(2)} stock=${product.stockCount} active=${product.isActive}`
  );
}
