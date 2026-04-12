import { HashCode } from "metano-runtime";

export class Shape {
  constructor(readonly x: number, readonly y: number) { }

  equals(other: any): boolean {
    return other instanceof Shape && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Shape>): Shape {
    return new Shape(overrides?.x ?? this.x, overrides?.y ?? this.y);
  }
}
