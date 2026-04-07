import { HashCode } from "@meta-sharp/runtime";
export class Coord {
  constructor(readonly x: number, readonly y: number) { }

  static moveX(coord: Coord, dx: number): Coord {
    return coord.with({ x: coord.x + dx });
  }

  equals(other: any): boolean {
    return other instanceof Coord && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);
    return hc.toHashCode();
  }

  with(overrides?: Partial<Coord>): Coord {
    return new Coord(overrides?.x ?? this.x, overrides?.y ?? this.y);
  }
}
