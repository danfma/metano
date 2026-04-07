import { HashCode } from "@meta-sharp/runtime";
export class Point {
  constructor(readonly x: number, readonly y: number) { }

  equals(other: any): boolean {
    return other instanceof Point && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);
    return hc.toHashCode();
  }

  with(overrides?: Partial<Point>): Point {
    return new Point(overrides?.x ?? this.x, overrides?.y ?? this.y);
  }
}
