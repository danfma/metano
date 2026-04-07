import { HashCode } from "@meta-sharp/runtime";
export class Vec2 {
  constructor(readonly x: number, readonly y: number) { }

  static __add(a: Vec2, b: Vec2): Vec2 {
    return new Vec2(a.x + b.x, a.y + b.y);
  }

  $add(b: Vec2): Vec2 {
    return Vec2.__add(this, b);
  }

  equals(other: any): boolean {
    return other instanceof Vec2 && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);
    return hc.toHashCode();
  }

  with(overrides?: Partial<Vec2>): Vec2 {
    return new Vec2(overrides?.x ?? this.x, overrides?.y ?? this.y);
  }
}
