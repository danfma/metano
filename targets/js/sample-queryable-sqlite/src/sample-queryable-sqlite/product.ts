/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";

let Product$rt: ClassType<Product> | undefined;

export class Product {
  constructor(
    readonly id: number,
    readonly name: string,
    readonly displayName: string,
    readonly unitPrice: Decimal,
    readonly stockCount: number,
    readonly isActive: boolean,
  ) {}

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
}
