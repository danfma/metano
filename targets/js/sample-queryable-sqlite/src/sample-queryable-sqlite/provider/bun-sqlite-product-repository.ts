/**
 * Concrete <see cref="IProductRepository"/> backed by `bun:sqlite`.
 *
 * The C# user code calls into the repository methods after composing
 * an `IQueryable<Product>` chain (`repo.Products.Where(...)`); on the
 * TS side that lowers to a `linq(...)` call which materializes the
 * iterable AND attaches the descriptor list via the runtime's
 * `LINQ_STAGES` symbol (#200). The repository reads the stage list,
 * routes it through the SQL translator, and runs the resulting
 * statement via `bun:sqlite`. When the chain has no introspectable
 * stages or the translator rejects a stage, the repository falls
 * back to the iterable's closure path so the call still produces the
 * correct result.
 */
import type { Database } from "bun:sqlite";
import { getStages } from "metano-runtime";
import type { Product } from "#/sample-queryable-sqlite/product";
import type { IProductRepository } from "#/sample-queryable-sqlite/provider/i-product-repository";
import {
  mapProductRow,
  productColumn,
  type ProductRow,
  SEED_ROWS,
} from "#/sample-queryable-sqlite/provider/db";
import {
  PROJECTION_ALIAS,
  type Stage,
  type TranslatedQuery,
  translateChain,
  UntranslatableTreeError,
} from "#/sample-queryable-sqlite/provider/sqlite-translator";

const TABLE = "products";

/**
 * Optional sink for the SQL the repository emits before each
 * execution. Tests leave it undefined for silent runs; the
 * `bun run .` entry hands `console.log` so the user sees the
 * generated query alongside the materialized result.
 */
export type SqlLogger = (sql: string, params: readonly unknown[]) => void;

export class BunSqliteProductRepository implements IProductRepository {
  /**
   * Surface the seeded array as the `Products` queryable. The C#
   * user code chains `Where` / `OrderBy` / etc on top of it; those
   * lower to <c>linq(repo.products, where(...))</c> on the TS side,
   * which materializes the iterable while exposing the descriptor
   * list for SQL translation.
   */
  readonly products: Iterable<Product>;

  constructor(
    private readonly db: Database,
    private readonly logger?: SqlLogger,
  ) {
    this.products = SEED_ROWS.map(toEntity);
  }

  toArray(query: Iterable<Product>): Product[] {
    const stages = getStages(query);
    if (stages !== undefined) {
      try {
        return this.executeRows(stages);
      } catch (err) {
        if (!(err instanceof UntranslatableTreeError)) throw err;
      }
    }
    return Array.from(query);
  }

  count(query: Iterable<Product>): number {
    const stages = getStages(query);
    if (stages !== undefined) {
      try {
        const translated = translateChain(TABLE, productColumn, [
          ...stages,
          // Synthesize a count terminal so the translator wraps the
          // query with `COUNT(*)` even when the chain only carried
          // composition stages.
          { kind: "count" } as Stage,
        ]);
        return this.executeCount(translated);
      } catch (err) {
        if (!(err instanceof UntranslatableTreeError)) throw err;
      }
    }
    return Array.from(query).length;
  }

  firstOrDefault(query: Iterable<Product>): Product | null {
    const stages = getStages(query);
    if (stages !== undefined) {
      try {
        const translated = translateChain(TABLE, productColumn, [
          ...stages,
          { kind: "firstOrDefault" } as Stage,
        ]);
        const row = this.executeFirst(translated);
        return row === undefined ? null : row;
      } catch (err) {
        if (!(err instanceof UntranslatableTreeError)) throw err;
      }
    }
    for (const item of query) return item;
    return null;
  }

  private executeRows(stages: readonly Stage[]): Product[] {
    const translated = translateChain(TABLE, productColumn, stages);
    this.logger?.(translated.sql, translated.params);
    const stmt = this.db.query(translated.sql);
    const rawRows = stmt.all(...bindable(translated.params)) as ProductRow[];
    if (translated.projected) {
      // Projection rows arrive aliased to PROJECTION_ALIAS by the
      // translator, so the lookup is by stable name rather than
      // insertion order. Cast widens to Product[] because the
      // adapter API today returns the entity shape — the C# user
      // gets a typed scalar projection through `Select` only at the
      // sample's `ActiveDisplayNames`-style chain.
      return rawRows.map(
        (row) => (row as unknown as Record<string, unknown>)[PROJECTION_ALIAS],
      ) as Product[];
    }
    return rawRows.map(mapProductRow);
  }

  private executeCount(translated: TranslatedQuery): number {
    this.logger?.(translated.sql, translated.params);
    const stmt = this.db.query(translated.sql);
    const row = stmt.get(...bindable(translated.params)) as { c?: number } | null;
    return row?.c ?? 0;
  }

  private executeFirst(translated: TranslatedQuery): Product | undefined {
    this.logger?.(translated.sql, translated.params);
    const stmt = this.db.query(translated.sql);
    const row = stmt.get(...bindable(translated.params)) as ProductRow | null;
    return row === null ? undefined : mapProductRow(row);
  }
}

/**
 * `bun:sqlite`'s bind signature is a strict union (numbers, strings,
 * booleans, bigints, Buffer, null). The translator pre-coerces values
 * via its own `toBindable` helper so the runtime cast here is safe;
 * the named helper isolates the cast to a single spot instead of
 * littering each call site with biome-ignore comments.
 */
type SqliteBindable = number | string | bigint | boolean | Buffer | null;

function bindable(params: readonly unknown[]): SqliteBindable[] {
  return params as SqliteBindable[];
}

function toEntity(row: (typeof SEED_ROWS)[number]): Product {
  return mapProductRow({
    id: row.id,
    name: row.name,
    display_name: row.displayName,
    unit_price: row.unitPrice,
    stock_count: row.stockCount,
    is_active: row.isActive ? 1 : 0,
  });
}
