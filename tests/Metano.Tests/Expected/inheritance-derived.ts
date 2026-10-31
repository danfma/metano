import { HashCode, copyRecordWith } from "metano-runtime";
import { Shape } from "./shape";

export class Circle extends Shape {
  constructor(x: number, y: number, readonly radius: number) {
    super(x, y);
  }

  area(): number {
    return 3.14159 * this.radius * this.radius;
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.radius === other.radius;
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
}
