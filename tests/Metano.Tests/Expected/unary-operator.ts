import { HashCode, copyRecordWith } from "metano-runtime";

export class Vec2 {
  constructor(readonly x: number, readonly y: number) { }

  static __negate(v: Vec2): Vec2 {
    return new Vec2(-v.x, -v.y);
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
}
