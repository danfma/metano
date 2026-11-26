import { HashCode, copyRecordWith } from "metano-runtime";

export class Shape {
  declare readonly x: number;
  declare readonly y: number;

  constructor(x: number, y: number) {
    this.x = x;
    this.y = y;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Shape>): Shape {
    return copyRecordWith<Shape>(this, overrides);
  }

  protected $printMembers(): string {
    return `X = ${this.x}, Y = ${this.y}`;
  }

  toString(): string {
    return `Shape { ${this.$printMembers()} }`;
  }
}
