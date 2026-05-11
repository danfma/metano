/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { linq, orderBy, orderByDescending, skip, take, where } from "metano-runtime/system/linq";
import type { Decimal } from "decimal.js";
import type { Product } from "#";
import type { IProductRepository } from "./i-product-repository";

export class ProductDemo {
  constructor() {}

  static activeProducts(repo: IProductRepository): Product[] {
    return repo.toArray(
      linq(
        repo.products,
        where((p: Product) => p.isActive, {
          tree: {
            kind: "member",
            target: {
              kind: "param",
              name: "p",
              type: { name: "Product" },
            },
            member: "isActive",
          },
        }),
      ),
    );
  }

  static inStock(repo: IProductRepository): Product[] {
    return repo.toArray(
      linq(
        repo.products,
        where((p: Product) => p.isActive && p.stockCount > 0, {
          tree: {
            kind: "binary",
            op: "&&",
            left: {
              kind: "member",
              target: {
                kind: "param",
                name: "p",
                type: { name: "Product" },
              },
              member: "isActive",
            },
            right: {
              kind: "binary",
              op: ">",
              left: {
                kind: "member",
                target: {
                  kind: "param",
                  name: "p",
                  type: { name: "Product" },
                },
                member: "stockCount",
              },
              right: {
                kind: "literal",
                value: 0,
                type: { name: "number" },
              },
            },
          },
        }),
      ),
    );
  }

  static atLeastPrice(repo: IProductRepository, minPrice: Decimal): Product[] {
    return repo.toArray(
      linq(
        repo.products,
        where((p: Product) => p.unitPrice.gte(minPrice), {
          tree: {
            kind: "binary",
            op: ">=",
            left: {
              kind: "member",
              target: {
                kind: "param",
                name: "p",
                type: { name: "Product" },
              },
              member: "unitPrice",
            },
            right: {
              kind: "capture",
              name: "minPrice",
              type: { name: "number" },
            },
          },
          captures: { minPrice: minPrice },
        }),
      ),
    );
  }

  static topByPrice(repo: IProductRepository, topN: number): Product[] {
    return repo.toArray(
      linq(
        repo.products,
        orderByDescending((p: Product) => p.unitPrice, {
          tree: {
            kind: "member",
            target: {
              kind: "param",
              name: "p",
              type: { name: "Product" },
            },
            member: "unitPrice",
          },
        }),
        take(topN),
      ),
    );
  }

  static page(repo: IProductRepository, offset: number, pageSize: number): Product[] {
    return repo.toArray(
      linq(
        repo.products,
        orderBy((p: Product) => p.id, {
          tree: {
            kind: "member",
            target: {
              kind: "param",
              name: "p",
              type: { name: "Product" },
            },
            member: "id",
          },
        }),
        skip(offset),
        take(pageSize),
      ),
    );
  }

  static countActive(repo: IProductRepository): number {
    return repo.count(
      linq(
        repo.products,
        where((p: Product) => p.isActive, {
          tree: {
            kind: "member",
            target: {
              kind: "param",
              name: "p",
              type: { name: "Product" },
            },
            member: "isActive",
          },
        }),
      ),
    );
  }

  static firstById(repo: IProductRepository): Product | null {
    return repo.firstOrDefault(
      linq(
        repo.products,
        orderBy((p: Product) => p.id, {
          tree: {
            kind: "member",
            target: {
              kind: "param",
              name: "p",
              type: { name: "Product" },
            },
            member: "id",
          },
        }),
      ),
    );
  }
}
