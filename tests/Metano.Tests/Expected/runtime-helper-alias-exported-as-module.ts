import { HashCode, copyRecordWith, valueEquals as valueEquals$rt } from "metano-runtime";

export function valueEquals(a: number, b: number): boolean {
  return a === b;
}

export class Inner {
  constructor(readonly n: number) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.n === other.n;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.n);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Inner>): Inner {
    return copyRecordWith<Inner>(this, overrides);
  }
}

export class Part {
  constructor(readonly item: Inner) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals$rt(this.item, other.item);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.item);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Part>): Part {
    return copyRecordWith<Part>(this, overrides);
  }
}
