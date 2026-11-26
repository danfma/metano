import { HashCode, copyRecordWith } from "metano-runtime";

export class Vec2 {
  declare readonly x: number;
  declare readonly y: number;

  constructor(x: number, y: number) {
    this.x = x;
    this.y = y;
  }

  static __negate(v: Vec2): Vec2 {
    return new Vec2(-v.x | 0, -v.y | 0);
  }

  $negate(): Vec2 {
    return Vec2.__negate(this);
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
