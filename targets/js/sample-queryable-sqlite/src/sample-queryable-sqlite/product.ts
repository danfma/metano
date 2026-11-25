/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  formatBoolean,
  formatText,
  valueEquals,
  type ClassType,
} from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";

let Product$rt: ClassType<Product> | undefined;

export class Product {
  declare readonly id: number;
  declare readonly name: string;
  declare readonly displayName: string;
  declare readonly unitPrice: Decimal;
  declare readonly stockCount: number;
  declare readonly isActive: boolean;

  constructor(
    id: number,
    name: string,
    displayName: string,
    unitPrice: Decimal,
    stockCount: number,
    isActive: boolean,
  ) {
    this.id = id;
    this.name = name;
    this.displayName = displayName;
    this.unitPrice = unitPrice;
    this.stockCount = stockCount;
    this.isActive = isActive;
  }

  static get $runtimeType(): ClassType<Product> {
    return (Product$rt ??= {
      kind: "class",
      name: "Product",
      fullName: "SampleQueryableSqlite.Product",
      namespace: "SampleQueryableSqlite",
      ctor: Product,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Product => v instanceof Product,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.name === other.name &&
      this.displayName === other.displayName &&
      valueEquals(this.unitPrice, other.unitPrice) &&
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
    return copyRecordWith<Product>(this, overrides);
  }

  toString(): string {
    return `Product { Id = ${this.id}, Name = ${this.name}, DisplayName = ${this.displayName}, UnitPrice = ${formatText(this.unitPrice)}, StockCount = ${this.stockCount}, IsActive = ${formatBoolean(this.isActive)} }`;
  }
}
