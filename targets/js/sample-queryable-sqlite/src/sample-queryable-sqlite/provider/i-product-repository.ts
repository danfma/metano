/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { implementsInterface, type InterfaceType } from "metano-runtime";
import type { Product } from "../product.js";

export interface IProductRepository {
  readonly products: Iterable<Product>;
  toArray(query: Iterable<Product>): Product[];
  count(query: Iterable<Product>): number;
  firstOrDefault(query: Iterable<Product>): Product | null;
}

export const IProductRepository$runtimeType: InterfaceType<IProductRepository> = {
  kind: "interface",
  name: "IProductRepository",
  fullName: "SampleQueryableSqlite.Provider.IProductRepository",
  namespace: "SampleQueryableSqlite.Provider",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IProductRepository =>
    implementsInterface(v, IProductRepository$runtimeType),
};
