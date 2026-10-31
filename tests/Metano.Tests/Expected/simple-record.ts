import { HashCode, copyRecordWith } from "metano-runtime";

export class Point {
  constructor(readonly x: number, readonly y: number) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Point>): Point {
    return copyRecordWith<Point>(this, overrides);
  }
}
