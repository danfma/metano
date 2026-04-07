import { HashCode } from "@meta-sharp/runtime";
import { Shape } from "./shape";
export class Circle extends Shape {
  constructor(readonly radius: number) {
    super(x, y);
  }

  area(): number {
    return 3.14159 * this.radius * this.radius;
  }

  equals(other: any): boolean {
    return other instanceof Circle && this.x === other.x && this.y === other.y && this.radius === other.radius;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);
    hc.add(this.radius);
    return hc.toHashCode();
  }

  with(overrides?: Partial<Circle>): Circle {
    return new Circle(overrides?.x ?? this.x, overrides?.y ?? this.y, overrides?.radius ?? this.radius);
  }
}
