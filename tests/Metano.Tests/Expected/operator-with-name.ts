import { HashCode, copyRecordWith } from "metano-runtime";

export class Vec2 {
  constructor(readonly x: number, readonly y: number) { }

  static __add(a: Vec2, b: Vec2): Vec2 {
    return new Vec2((a.x + b.x) | 0, (a.y + b.y) | 0);
  }

  $add(b: Vec2): Vec2 {
    return Vec2.__add(this, b);
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

  with(overrides?: Partial<Vec2>): Vec2 {
    return copyRecordWith<Vec2>(this, overrides);
  }

  toString(): string {
    return `Vec2 { X = ${this.x}, Y = ${this.y} }`;
  }
}
