import { HashCode, copyRecordWith } from "metano-runtime";

export class Item {
  constructor(readonly name: string, readonly qty: number) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.name === other.name && this.qty === other.qty;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.name);
    hc.add(this.qty);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Item>): Item {
    return copyRecordWith<Item>(this, overrides);
  }

  protected $printMembers(): string {
    return `Name = ${this.name ?? ""}, Qty = ${this.qty}`;
  }

  toString(): string {
    return `Item { ${this.$printMembers()} }`;
  }
}
