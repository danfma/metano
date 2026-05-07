/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode } from "metano-runtime";
import type { Decimal } from "decimal.js";

export class Product {
  constructor(
    readonly id: number,
    readonly name: string,
    readonly displayName: string,
    readonly unitPrice: Decimal,
    readonly stockCount: number,
    readonly isActive: boolean,
  ) {}

  equals(other: any): boolean {
    return (
      other instanceof Product &&
      this.id === other.id &&
      this.name === other.name &&
      this.displayName === other.displayName &&
      this.unitPrice === other.unitPrice &&
      this.stockCount === other.stockCount &&
      this.isActive === other.isActive
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.name);
    hc.add(this.displayName);
    hc.add(this.unitPrice);
    hc.add(this.stockCount);
    hc.add(this.isActive);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Product>): Product {
    return new Product(
      overrides?.id ?? this.id,
      overrides?.name ?? this.name,
      overrides?.displayName ?? this.displayName,
      overrides?.unitPrice ?? this.unitPrice,
      overrides?.stockCount ?? this.stockCount,
      overrides?.isActive ?? this.isActive,
    );
  }
}
