import { HashCode, copyRecordWith, formatDouble, valueEquals } from "metano-runtime";
import { Shape } from "./shape.js";

export class Circle extends Shape {
  declare readonly radius: number;

  constructor(x: number, y: number, radius: number) {
    super(x, y);
    this.radius = radius;
  }

  area(): number {
    return 3.14159 * this.radius * this.radius;
  }

  override equals(other: any): boolean {
    return super.equals(other) && valueEquals(this.radius, other.radius);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.radius);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Circle>): Circle {
    return copyRecordWith<Circle>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Radius = ${formatDouble(this.radius)}`;
  }

  override toString(): string {
    return `Circle { ${this.$printMembers()} }`;
  }
}
