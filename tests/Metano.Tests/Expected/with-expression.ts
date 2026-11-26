import { HashCode, copyRecordWith } from "metano-runtime";

export class Coord {
  declare readonly x: number;
  declare readonly y: number;

  constructor(x: number, y: number) {
    this.x = x;
    this.y = y;
  }

  static moveX(coord: Coord, dx: number): Coord {
    return coord.with({ x: (coord.x + dx) | 0 });
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

  with(overrides?: Partial<Coord>): Coord {
    return copyRecordWith<Coord>(this, overrides);
  }

  toString(): string {
    return `Coord { X = ${this.x}, Y = ${this.y} }`;
  }
}
