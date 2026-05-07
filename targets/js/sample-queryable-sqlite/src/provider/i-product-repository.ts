/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Product } from "#";

export interface IProductRepository {
  readonly products: Iterable<Product>;
  toArray(query: Iterable<Product>): Product[];
  count(query: Iterable<Product>): number;
  firstOrDefault(query: Iterable<Product>): Product | null;
}
